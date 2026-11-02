using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.Products;

public sealed record ProductInput(string? Code, string? Name, string? Spec, string? Unit, string? Category, decimal? DefaultPrice, decimal? DefaultCost, bool? IsActive);

/// <summary>
/// 品項價目表（company scope）。報價編輯器以 keyword 查詢帶入品名、規格、單位與單價；
/// 預設成本只回給有 <c>quotes:view_cost</c> 的人。
/// </summary>
[Route("api/products")]
public sealed class ProductsController(ProductService service) : ScopedControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Search([FromQuery] string? keyword, [FromQuery] bool? activeOnly, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        TryCompany("products", "read", out var scope) ? Ok(await service.SearchAsync(scope, keyword, activeOnly ?? false, page ?? 1, pageSize ?? 20, cancellationToken)) : Forbidden();

    [HttpPost]
    public async Task<ActionResult> Create(ProductInput input, CancellationToken cancellationToken) =>
        TryCompany("products", "create", out var scope) ? ToResult(await service.SaveAsync(scope, null, input, cancellationToken)) : Forbidden();

    [HttpPut("{productId:guid}")]
    public async Task<ActionResult> Update(Guid productId, ProductInput input, CancellationToken cancellationToken) =>
        TryCompany("products", "update", out var scope) ? ToResult(await service.SaveAsync(scope, productId, input, cancellationToken)) : Forbidden();

    /// <summary>已被報價引用的品項改為停用，避免歷史報價失去關聯。</summary>
    [HttpDelete("{productId:guid}")]
    public async Task<ActionResult> Delete(Guid productId, CancellationToken cancellationToken) =>
        TryCompany("products", "delete", out var scope) ? ToResult(await service.DeleteAsync(scope, productId, cancellationToken)) : Forbidden();
}

/// <summary>品項價目表。</summary>
public sealed class ProductService(QuoteDbContext db, AuditService audit)
{
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
