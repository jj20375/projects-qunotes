using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Products;

/// <summary>
/// 品項價目資料輸入契約。
/// </summary>
/// <param name="Code">品號料號代碼。</param>
/// <param name="Name">品項名稱。</param>
/// <param name="Spec">規格尺寸說明。</param>
/// <param name="Unit">計量單位。</param>
/// <param name="Category">品項分類類別。</param>
/// <param name="DefaultPrice">預設銷售單價。</param>
/// <param name="DefaultCost">預設採購成本（僅具 view_cost 權限者可寫入）。</param>
/// <param name="IsActive">是否啟用。</param>
public sealed record ProductInput(string? Code, string? Name, string? Spec, string? Unit, string? Category, decimal? DefaultPrice, decimal? DefaultCost, bool? IsActive);

/// <summary>
/// 品項價目表管理控制器（公司租戶範圍）。
/// 供報價編輯器以關鍵字快速帶入品名、規格、單位與單價；預設成本欄位受 quotes:view_cost 權限保護。
/// </summary>
/// <param name="service">品項領域核心服務實體。</param>
[Route("api/products")]
public sealed class ProductsController(ProductService service) : ScopedControllerBase
{
    /// <summary>
    /// 條件搜尋品項價目清單並分頁回傳。
    /// </summary>
    /// <param name="keyword">搜尋關鍵字（比對品號、品名、規格或分類）。</param>
    /// <param name="activeOnly">是否僅查詢啟用中之品項。</param>
    /// <param name="page">頁碼（預設為 1）。</param>
    /// <param name="pageSize">每頁筆數（預設為 20）。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回分頁品項列表與筆數合計。</returns>
    [HttpGet]
    public async Task<ActionResult> Search([FromQuery] string? keyword, [FromQuery] bool? activeOnly, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        TryCompany("products", "read", out var scope) ? Ok(await service.SearchAsync(scope, keyword, activeOnly ?? false, page ?? 1, pageSize ?? 20, cancellationToken)) : Forbidden();

    /// <summary>
    /// 建立新品項。
    /// </summary>
    /// <param name="input">品項輸入資料。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回建立之品項 ID 物件。</returns>
    [HttpPost]
    public async Task<ActionResult> Create(ProductInput input, CancellationToken cancellationToken) =>
        TryCompany("products", "create", out var scope) ? ToResult(await service.SaveAsync(scope, null, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 更新既有品項資料。
    /// </summary>
    /// <param name="productId">品項唯一識別碼。</param>
    /// <param name="input">品項更新內容。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回更新結果。</returns>
    [HttpPut("{productId:guid}")]
    public async Task<ActionResult> Update(Guid productId, ProductInput input, CancellationToken cancellationToken) =>
        TryCompany("products", "update", out var scope) ? ToResult(await service.SaveAsync(scope, productId, input, cancellationToken)) : Forbidden();

    /// <summary>
    /// 刪除品項；若已被報價單引用則自動轉為停用，避免歷史單據斷鏈。
    /// </summary>
    /// <param name="productId">品項唯一識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回刪除或停用結果標記。</returns>
    [HttpDelete("{productId:guid}")]
    public async Task<ActionResult> Delete(Guid productId, CancellationToken cancellationToken) =>
        TryCompany("products", "delete", out var scope) ? ToResult(await service.DeleteAsync(scope, productId, cancellationToken)) : Forbidden();
}

/// <summary>
/// 品項價目領域核心業務服務。
/// </summary>
/// <param name="db">資料庫上下文實體。</param>
/// <param name="audit">稽核紀錄服務實體。</param>
public sealed class ProductService(QuoteDbContext db, AuditService audit)
{
    /// <summary>
    /// 搜尋品項價目清單。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="keyword">搜尋關鍵字。</param>
    /// <param name="activeOnly">是否僅查詢啟用者。</param>
    /// <param name="page">頁碼。</param>
    /// <param name="pageSize">每頁筆數。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回包含分頁資訊與品項清單之匿名物件。</returns>
    public async Task<object> SearchAsync(RequestScope scope, string? keyword, bool activeOnly, int page, int pageSize, CancellationToken cancellationToken)
    {
        var products = db.Products.AsNoTracking();
        if (activeOnly) products = products.Where(value => value.IsActive);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var text = keyword.Trim();
            products = products.Where(value => value.Name.Contains(text) || value.Code.Contains(text) || (value.Spec != null && value.Spec.Contains(text)) || (value.Category != null && value.Category.Contains(text)));
        }
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);
        var total = await products.CountAsync(cancellationToken);
        var rows = await products.OrderBy(value => value.Code).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var canSeeCost = scope.HasOption("quotes", "view_cost");
        return new
        {
            total,
            page,
            pageSize,
            items = rows.Select(value => new { value.Id, value.Code, value.Name, value.Spec, value.Unit, value.Category, value.DefaultPrice, defaultCost = canSeeCost ? value.DefaultCost : null, value.IsActive }),
        };
    }

    /// <summary>
    /// 新增或儲存品項資料。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="productId">品項 ID（若為新增則為 null）。</param>
    /// <param name="input">品項資料輸入物件。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回儲存後之服務結果。</returns>
    public async Task<ServiceResult<object>> SaveAsync(RequestScope scope, Guid? productId, ProductInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.Name)) return ServiceResult<object>.BadRequest("請輸入品號與品名。");
        if (input.DefaultPrice < 0 || input.DefaultCost < 0) return ServiceResult<object>.BadRequest("單價與成本不可為負數。");
        var code = input.Code.Trim();
        if (await db.Products.AnyAsync(value => value.Code == code && value.Id != productId, cancellationToken)) return ServiceResult<object>.Conflict("品號已被使用。");
        var product = productId is Guid id ? await db.Products.SingleOrDefaultAsync(value => value.Id == id, cancellationToken) : null;
        if (productId is not null && product is null) return ServiceResult<object>.NotFound("找不到品項。");
        if (product is null)
        {
            product = new Product();
            db.Products.Add(product);
        }
        product.Code = code;
        product.Name = input.Name.Trim();
        product.Spec = Clean(input.Spec);
        product.Unit = Clean(input.Unit);
        product.Category = Clean(input.Category);
        product.DefaultPrice = input.DefaultPrice ?? 0;
        // 看不到成本的人修改品項時，不覆寫既有成本。
        if (scope.HasOption("quotes", "view_cost")) product.DefaultCost = input.DefaultCost;
        product.IsActive = input.IsActive ?? product.IsActive;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "product", product.Id, productId is null ? "create" : "update", after: new { product.Code, product.Name, product.DefaultPrice });
        await db.SaveChangesAsync(cancellationToken);
        return productId is null ? ServiceResult<object>.Created(new { product.Id }) : ServiceResult<object>.Ok(new { product.Id });
    }

    /// <summary>
    /// 刪除指定品項；若已有報價引用則轉為停用。
    /// </summary>
    /// <param name="scope">當前已驗證之請求範圍。</param>
    /// <param name="productId">品項識別碼。</param>
    /// <param name="cancellationToken">非同步取消權杖。</param>
    /// <returns>傳回刪除/停用結果。</returns>
    public async Task<ServiceResult<object>> DeleteAsync(RequestScope scope, Guid productId, CancellationToken cancellationToken)
    {
        var product = await db.Products.SingleOrDefaultAsync(value => value.Id == productId, cancellationToken);
        if (product is null) return ServiceResult<object>.NotFound("找不到品項。");
        var used = await db.QuoteItems.AnyAsync(value => value.ProductId == productId, cancellationToken);
        if (used) product.IsActive = false;
        else db.Products.Remove(product);
        audit.Add(scope.CompanyId, scope.UserId, "product", productId, used ? "deactivate" : "delete", new { product.Code, product.Name });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<object>.Ok(new { deleted = !used, deactivated = used });
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
