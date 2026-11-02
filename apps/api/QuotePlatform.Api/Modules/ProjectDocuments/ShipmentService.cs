using Microsoft.EntityFrameworkCore;
using QuotePlatform.Api.Infrastructure;
using QuotePlatform.Api.Modules.Common;

namespace QuotePlatform.Api.Modules.ProjectDocuments;

/// <summary>
/// 出貨單：掛在訂單下（一張訂單可分批出貨），一律對應同專案的訂單。
/// 可見性跟著專案的資料範圍；權限沿用 <c>orders</c>（出貨是訂單履約的一部分）。
/// </summary>
public sealed class ShipmentService(QuoteDbContext db, ProjectAccessService access, ProjectDocumentService documents, AuditService audit)
{
    private static readonly string[] Statuses = ["shipped", "delivered", "cancelled"];

    public async Task<ServiceResult<object>> ListAsync(RequestScope scope, Guid projectId, ListQuery query, CancellationToken cancellationToken)
    {
        var project = await access.GetAsync(scope, projectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到專案。");
        var shipments = db.ProjectShipments.AsNoTracking().Where(value => value.ProjectId == projectId);
        if (query.KeywordText is string keyword)
        {
            shipments = shipments.Where(value =>
                value.ShipmentNo.Contains(keyword)
                || (value.ReceiverName != null && value.ReceiverName.Contains(keyword))
                || (value.TrackingNo != null && value.TrackingNo.Contains(keyword)));
        }
        var total = await shipments.CountAsync(cancellationToken);
        var rows = await shipments.OrderByDescending(value => value.ShipDate).ThenByDescending(value => value.ShipmentNo).Skip(query.Skip).Take(query.PageSizeValue).ToListAsync(cancellationToken);
        var orderNos = await documents.OrderNosAsync(rows.Select(value => value.OrderId), cancellationToken);
        var counts = await documents.AttachmentCountsAsync("shipment", rows.Select(value => value.Id), cancellationToken);
        return ServiceResult<object>.Ok(new
        {
            canCreate = project.CanWriteChild(scope, "orders", "create"),
            canUpdate = project.CanWriteChild(scope, "orders"),
            canDelete = project.CanWriteChild(scope, "orders", "delete"),
            total,
            page = query.PageValue,
            pageSize = query.PageSizeValue,
            items = rows.Select(value => new
            {
                value.Id,
                value.OrderId,
                orderNo = orderNos.GetValueOrDefault(value.OrderId),
                value.ShipmentNo,
                value.ShipDate,
                value.Status,
                value.ReceiverName,
                value.DeliveryAddress,
                value.TrackingNo,
                value.Note,
                attachmentCount = counts.GetValueOrDefault(value.Id),
            }),
        });
    }

    public async Task<ServiceResult<object>> SaveAsync(RequestScope scope, Guid? projectId, Guid? shipmentId, ShipmentInput input, CancellationToken cancellationToken)
    {
        var shipment = shipmentId is Guid id ? await db.ProjectShipments.SingleOrDefaultAsync(value => value.Id == id, cancellationToken) : null;
        if (shipmentId is not null && shipment is null) return ServiceResult<object>.NotFound("找不到出貨單。");
        var project = await access.GetAsync(scope, shipment?.ProjectId ?? projectId!.Value, track: false, cancellationToken);
        if (project is null) return ServiceResult<object>.NotFound("找不到專案。");
        if (!project.CanWriteChild(scope, "orders", shipment is null ? "create" : "update")) return ServiceResult<object>.Forbidden("您對此專案的出貨單只有檢視權限。");

        if (string.IsNullOrWhiteSpace(input.ShipmentNo) || input.ShipDate is null) return ServiceResult<object>.BadRequest("請輸入出貨單號與出貨日期。");
        if (input.Status is not null && !Statuses.Contains(input.Status)) return ServiceResult<object>.BadRequest("出貨狀態不正確。");
        // 出貨單一定要有訂單，沒有舊資料的寬容規則。
        if (input.OrderId is null) return ServiceResult<object>.BadRequest("請選擇對應的訂單。");
        if (await documents.CheckOrderAsync(project.Project.Id, input.OrderId, shipment?.OrderId, shipment is null, cancellationToken) is string orderError) return ServiceResult<object>.BadRequest(orderError);

        var isNew = shipment is null;
        if (shipment is null)
        {
            shipment = new ProjectShipment { ProjectId = project.Project.Id, CreatedBy = scope.UserId };
            db.ProjectShipments.Add(shipment);
        }
        shipment.OrderId = input.OrderId.Value;
        shipment.ShipmentNo = input.ShipmentNo.Trim();
        shipment.ShipDate = input.ShipDate.Value;
        shipment.Status = input.Status ?? shipment.Status;
        shipment.ReceiverName = ProjectDocumentService.Clean(input.ReceiverName);
        shipment.DeliveryAddress = ProjectDocumentService.Clean(input.DeliveryAddress);
        shipment.TrackingNo = ProjectDocumentService.Clean(input.TrackingNo);
        shipment.Note = ProjectDocumentService.Clean(input.Note);
        shipment.UpdatedBy = scope.UserId;
        shipment.UpdatedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "shipment", shipment.Id, isNew ? "create" : "update", after: new { shipment.ShipmentNo, shipment.OrderId, shipment.ShipDate, shipment.Status });
        await db.SaveChangesAsync(cancellationToken);
        return isNew ? ServiceResult<object>.Created(new { shipment.Id }) : ServiceResult<object>.Ok(new { shipment.Id });
    }

    public async Task<ServiceResult<Empty>> DeleteAsync(RequestScope scope, Guid shipmentId, CancellationToken cancellationToken)
    {
        var shipment = await db.ProjectShipments.SingleOrDefaultAsync(value => value.Id == shipmentId, cancellationToken);
        if (shipment is null) return ServiceResult<Empty>.NotFound("找不到出貨單。");
        var project = await access.GetAsync(scope, shipment.ProjectId, track: false, cancellationToken);
        if (project is null) return ServiceResult<Empty>.NotFound("找不到出貨單。");
        if (!project.CanWriteChild(scope, "orders", "delete")) return ServiceResult<Empty>.Forbidden("沒有刪除出貨單的權限。");
        shipment.IsDeleted = true;
        shipment.DeletedAt = DateTimeOffset.UtcNow;
        audit.Add(scope.CompanyId, scope.UserId, "shipment", shipmentId, "delete", new { shipment.ShipmentNo, shipment.OrderId });
        await db.SaveChangesAsync(cancellationToken);
        return ServiceResult<Empty>.Ok(Empty.Value);
    }
}
