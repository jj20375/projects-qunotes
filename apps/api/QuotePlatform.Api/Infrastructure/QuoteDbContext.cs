using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace QuotePlatform.Api.Infrastructure;

/// <summary>
/// 共用資料庫 <c>quote_platform</c> 的 EF Core context。
/// </summary>
/// <remarks>
/// 所有 <see cref="ICompanyOwned"/> entity 都套用 Global Query Filter：<c>CompanyId == 目前 JWT 的 company_id</c>；
/// 沒有公司範圍（未登入、platform scope）時查不到任何公司資料，必須明確使用 <c>IgnoreQueryFilters()</c>。
/// 寫入時由 <see cref="EnforceCompanyOwnership"/> 填入或檢查 CompanyId，前端傳入的值不會被信任。
/// </remarks>
public sealed class QuoteDbContext(DbContextOptions<QuoteDbContext> options, CompanyContext? companyContext = null) : DbContext(options)
{
    private static readonly MethodInfo ApplyCompanyFilterMethod =
        typeof(QuoteDbContext).GetMethod(nameof(ApplyCompanyFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanySetting> CompanySettings => Set<CompanySetting>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<DepartmentClosure> DepartmentClosures => Set<DepartmentClosure>();
    public DbSet<CompanyMembership> CompanyMemberships => Set<CompanyMembership>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientContact> ClientContacts => Set<ClientContact>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<ProjectStatusHistory> ProjectStatusHistories => Set<ProjectStatusHistory>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteItem> QuoteItems => Set<QuoteItem>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ImportMappingProfile> ImportMappingProfiles => Set<ImportMappingProfile>();
    public DbSet<FileObject> Files => Set<FileObject>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<ProjectOrder> ProjectOrders => Set<ProjectOrder>();
    public DbSet<ProjectShipment> ProjectShipments => Set<ProjectShipment>();
    public DbSet<ProjectWarranty> ProjectWarranties => Set<ProjectWarranty>();
    public DbSet<ProjectInvoice> ProjectInvoices => Set<ProjectInvoice>();

    /// <summary>Global Query Filter 參考的公司；EF Core 每次查詢都會重新讀取這個屬性。</summary>
    public Guid? CurrentCompanyId => companyContext?.CompanyId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Account).HasMaxLength(100).IsRequired();
            entity.Property(value => value.Email).HasMaxLength(200);
            entity.Property(value => value.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(value => value.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(value => value.Account).IsUnique();
            entity.HasIndex(value => value.Email);
            entity.HasOne(value => value.SystemRole).WithMany().HasForeignKey(value => value.SystemRoleId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
            entity.Property(value => value.CreatedIp).HasMaxLength(64);
            entity.Property(value => value.UserAgent).HasMaxLength(500);
            entity.HasIndex(value => value.TokenHash).IsUnique();
            entity.HasIndex(value => new { value.UserId, value.ExpiresAt });
            entity.HasOne<User>().WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("companies");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Name).HasMaxLength(200).IsRequired();
            entity.Property(value => value.Slug).HasMaxLength(100).IsRequired();
            entity.Property(value => value.TaxId).HasMaxLength(20);
            entity.Property(value => value.Phone).HasMaxLength(50);
            entity.Property(value => value.Address).HasMaxLength(500);
            entity.Property(value => value.EnglishName).HasMaxLength(200);
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(value => value.Slug).IsUnique();
            entity.HasIndex(value => value.RootAdminMembershipId).IsUnique();
            entity.HasOne<CompanyMembership>().WithOne().HasForeignKey<Company>(value => value.RootAdminMembershipId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CompanySetting>(entity =>
        {
            entity.ToTable("company_settings");
            entity.HasKey(value => value.CompanyId);
            entity.Property(value => value.DefaultTaxRate).HasPrecision(5, 2);
            entity.Property(value => value.DefaultCurrency).HasMaxLength(3).IsFixedLength();
            entity.Property(value => value.PdfHeaderJson).HasColumnType("longtext");
            entity.Property(value => value.QuoteNumberPattern).HasMaxLength(40).IsRequired().HasDefaultValue(DocumentNumberPattern.DefaultQuote);
            entity.HasOne<Company>().WithOne().HasForeignKey<CompanySetting>(value => value.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("departments");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Code).HasMaxLength(50);
            entity.Property(value => value.Name).HasMaxLength(100).IsRequired();
            entity.HasIndex(value => new { value.CompanyId, value.Code }).IsUnique();
            entity.HasIndex(value => new { value.CompanyId, value.ParentId });
            entity.HasOne<Company>().WithMany().HasForeignKey(value => value.CompanyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Department>().WithMany().HasForeignKey(value => value.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DepartmentClosure>(entity =>
        {
            entity.ToTable("department_closure");
            entity.HasKey(value => new { value.AncestorId, value.DescendantId });
            entity.HasIndex(value => new { value.AncestorId, value.Depth });
            entity.HasIndex(value => new { value.DescendantId, value.Depth });
            entity.HasOne<Department>().WithMany().HasForeignKey(value => value.AncestorId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Department>().WithMany().HasForeignKey(value => value.DescendantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CompanyMembership>(entity =>
        {
            entity.ToTable("company_memberships");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.DataScopeOverride).HasMaxLength(20);
            entity.HasIndex(value => new { value.CompanyId, value.UserId }).IsUnique();
            entity.HasIndex(value => new { value.CompanyId, value.DepartmentId });
            entity.HasOne<Company>().WithMany().HasForeignKey(value => value.CompanyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(value => value.User).WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Department>().WithMany().HasForeignKey(value => value.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(value => value.Role).WithMany().HasForeignKey(value => value.RoleId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Scope).HasMaxLength(20).IsRequired();
            entity.Property(value => value.Code).HasMaxLength(100).IsRequired();
            entity.Property(value => value.Name).HasMaxLength(100).IsRequired();
            entity.Property(value => value.DataScope).HasMaxLength(20).IsRequired();
            // CompanyId 為 NULL 的系統角色不受 unique 約束，代碼唯一由 Seeder 與 RoleService 保證。
            entity.HasIndex(value => new { value.CompanyId, value.Code }).IsUnique();
            entity.HasOne<Company>().WithMany().HasForeignKey(value => value.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.ToTable("permissions");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Code).HasMaxLength(100).IsRequired();
            entity.Property(value => value.Name).HasMaxLength(100).IsRequired();
            entity.Property(value => value.Description).HasMaxLength(500);
            entity.Property(value => value.Scope).HasMaxLength(20).IsRequired();
            entity.Property(value => value.RoutePath).HasMaxLength(200);
            entity.Property(value => value.SubFeaturesJson).HasColumnType("longtext");
            entity.HasIndex(value => value.Code).IsUnique();
            entity.HasOne<Permission>().WithMany().HasForeignKey(value => value.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("role_permissions");
            entity.HasKey(value => new { value.RoleId, value.PermissionId });
            entity.Property(value => value.OptionsJson).HasColumnType("longtext");
            entity.HasOne<Role>().WithMany().HasForeignKey(value => value.RoleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(value => value.Permission).WithMany().HasForeignKey(value => value.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserPermission>(entity =>
        {
            entity.ToTable("user_permissions");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Effect).HasMaxLength(10).IsRequired();
            entity.Property(value => value.OptionsJson).HasColumnType("longtext");
            entity.Property(value => value.Reason).HasMaxLength(500);
            entity.HasIndex(value => new { value.CompanyId, value.UserId, value.PermissionId, value.Effect }).IsUnique();
            entity.HasOne<Company>().WithMany().HasForeignKey(value => value.CompanyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(value => value.Permission).WithMany().HasForeignKey(value => value.PermissionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.EntityType).HasMaxLength(50).IsRequired();
            entity.Property(value => value.Action).HasMaxLength(100).IsRequired();
            entity.Property(value => value.BeforeJson).HasColumnType("longtext");
            entity.Property(value => value.AfterJson).HasColumnType("longtext");
            entity.Property(value => value.Ip).HasMaxLength(64);
            entity.HasIndex(value => new { value.CompanyId, value.EntityType, value.EntityId });
            entity.HasIndex(value => new { value.CompanyId, value.CreatedAt });
            entity.HasIndex(value => value.ActorUserId);
        });

        ConfigureBusiness(modelBuilder);
        ConfigureQuotes(modelBuilder);
        ConfigureDocuments(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(value => typeof(ICompanyOwned).IsAssignableFrom(value.ClrType)))
        {
            var method = typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType) ? ApplySoftDeleteCompanyFilterMethod : ApplyCompanyFilterMethod;
            method.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }
    }

    private static readonly MethodInfo ApplySoftDeleteCompanyFilterMethod =
        typeof(QuoteDbContext).GetMethod(nameof(ApplySoftDeleteCompanyFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private void ApplyCompanyFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, ICompanyOwned =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(value => value.CompanyId == CurrentCompanyId);

    /// <summary>EF Core 每個 entity 只能有一個 query filter，軟刪除條件必須與公司條件合併。</summary>
    private void ApplySoftDeleteCompanyFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, ICompanyOwned, ISoftDelete =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(value => value.CompanyId == CurrentCompanyId && !value.IsDeleted);

    /// <summary>第四階段：檔案、附件、訂單、保固與發票。</summary>
    private static void ConfigureDocuments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileObject>(entity =>
        {
            entity.ToTable("files");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.StorageKey).HasMaxLength(500).IsRequired();
            entity.Property(value => value.OriginalName).HasMaxLength(255).IsRequired();
            entity.Property(value => value.ContentType).HasMaxLength(150).IsRequired();
            entity.Property(value => value.Sha256).HasMaxLength(64).IsFixedLength();
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(value => value.StorageKey).IsUnique();
            entity.HasIndex(value => new { value.CompanyId, value.Status });
            entity.HasOne<Company>().WithMany().HasForeignKey(value => value.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Attachment>(entity =>
        {
            entity.ToTable("attachments");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.OwnerType).HasMaxLength(20).IsRequired();
            entity.Property(value => value.Category).HasMaxLength(50);
            entity.Property(value => value.Note).HasMaxLength(500);
            entity.HasIndex(value => new { value.CompanyId, value.OwnerType, value.OwnerId });
            entity.HasOne(value => value.File).WithMany().HasForeignKey(value => value.FileId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProjectOrder>(entity =>
        {
            entity.ToTable("project_orders");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.OrderNo).HasMaxLength(100).IsRequired();
            entity.Property(value => value.Amount).HasPrecision(18, 2);
            entity.Property(value => value.TaxAmount).HasPrecision(18, 2);
            entity.Property(value => value.Total).HasPrecision(18, 2);
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.Property(value => value.Note).HasColumnType("text");
            entity.HasIndex(value => new { value.CompanyId, value.ProjectId });
            entity.HasIndex(value => new { value.CompanyId, value.OrderDate });
            entity.HasOne<Project>().WithMany().HasForeignKey(value => value.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Quote>().WithMany().HasForeignKey(value => value.QuoteId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ProjectShipment>(entity =>
        {
            entity.ToTable("project_shipments");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.ShipmentNo).HasMaxLength(100).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.Property(value => value.ReceiverName).HasMaxLength(100);
            entity.Property(value => value.DeliveryAddress).HasMaxLength(300);
            entity.Property(value => value.TrackingNo).HasMaxLength(100);
            entity.Property(value => value.Note).HasColumnType("text");
            entity.HasIndex(value => new { value.CompanyId, value.ProjectId });
            entity.HasIndex(value => new { value.CompanyId, value.OrderId });
            entity.HasOne<Project>().WithMany().HasForeignKey(value => value.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProjectOrder>().WithMany().HasForeignKey(value => value.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProjectWarranty>(entity =>
        {
            entity.ToTable("project_warranties");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.WarrantyNo).HasMaxLength(100).IsRequired();
            entity.Property(value => value.Subject).HasMaxLength(200).IsRequired();
            entity.Property(value => value.Terms).HasColumnType("text");
            entity.Property(value => value.Note).HasColumnType("text");
            entity.HasIndex(value => new { value.CompanyId, value.ProjectId });
            entity.HasIndex(value => new { value.CompanyId, value.EndDate });
            entity.HasOne<Project>().WithMany().HasForeignKey(value => value.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProjectOrder>().WithMany().HasForeignKey(value => value.OrderId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ProjectInvoice>(entity =>
        {
            entity.ToTable("project_invoices");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.InvoiceNo).HasMaxLength(20).IsRequired();
            entity.Property(value => value.InvoiceType).HasMaxLength(20).IsRequired();
            entity.Property(value => value.BuyerName).HasMaxLength(200);
            entity.Property(value => value.BuyerTaxId).HasMaxLength(20);
            entity.Property(value => value.SalesAmount).HasPrecision(18, 2);
            entity.Property(value => value.TaxAmount).HasPrecision(18, 2);
            entity.Property(value => value.TotalAmount).HasPrecision(18, 2);
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.Property(value => value.PaymentStatus).HasMaxLength(20).IsRequired();
            entity.Property(value => value.PaidAmount).HasPrecision(18, 2);
            entity.Property(value => value.VoidReason).HasMaxLength(500);
            entity.Property(value => value.Note).HasColumnType("text");
            entity.HasIndex(value => new { value.CompanyId, value.InvoiceNo }).IsUnique();
            entity.HasIndex(value => new { value.CompanyId, value.ProjectId });
            entity.HasIndex(value => new { value.CompanyId, value.InvoiceDate });
            entity.HasIndex(value => new { value.CompanyId, value.PaymentStatus, value.DueDate });
            entity.HasOne<Project>().WithMany().HasForeignKey(value => value.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProjectOrder>().WithMany().HasForeignKey(value => value.OrderId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    /// <summary>第三階段：報價、品項與匯入。</summary>
    private static void ConfigureQuotes(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Quote>(entity =>
        {
            entity.ToTable("quotes");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.QuoteNo).HasMaxLength(50).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.Property(value => value.Title).HasMaxLength(200);
            entity.Property(value => value.ClientName).HasMaxLength(200).IsRequired();
            entity.Property(value => value.ClientTaxId).HasMaxLength(20);
            entity.Property(value => value.ClientAddress).HasMaxLength(500);
            entity.Property(value => value.ContactName).HasMaxLength(100);
            entity.Property(value => value.ContactPhone).HasMaxLength(50);
            entity.Property(value => value.ContactEmail).HasMaxLength(200);
            entity.Property(value => value.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(value => value.ExchangeRate).HasPrecision(18, 6);
            entity.Property(value => value.Subtotal).HasPrecision(18, 2);
            entity.Property(value => value.DiscountType).HasMaxLength(10).IsRequired();
            entity.Property(value => value.DiscountValue).HasPrecision(18, 4);
            entity.Property(value => value.DiscountAmount).HasPrecision(18, 2);
            entity.Property(value => value.TaxType).HasMaxLength(10).IsRequired();
            entity.Property(value => value.TaxRate).HasPrecision(5, 2);
            entity.Property(value => value.NetAmount).HasPrecision(18, 2);
            entity.Property(value => value.TaxAmount).HasPrecision(18, 2);
            entity.Property(value => value.Total).HasPrecision(18, 2);
            entity.Property(value => value.CostTotal).HasPrecision(18, 2);
            entity.Property(value => value.GrossMargin).HasPrecision(18, 2);
            entity.Property(value => value.PaymentTerms).HasMaxLength(500);
            entity.Property(value => value.DeliveryTerms).HasMaxLength(500);
            entity.Property(value => value.Terms).HasColumnType("text");
            entity.Property(value => value.Note).HasColumnType("text");
            entity.Property(value => value.Source).HasMaxLength(20).IsRequired();
            entity.Property(value => value.StatusReason).HasMaxLength(500);
            entity.HasIndex(value => new { value.CompanyId, value.QuoteNo, value.RevisionNo }).IsUnique();
            entity.HasIndex(value => new { value.CompanyId, value.ProjectId });
            entity.HasIndex(value => new { value.CompanyId, value.Status, value.QuoteDate });
            entity.HasIndex(value => new { value.CompanyId, value.AcceptedAt });
            entity.HasIndex(value => new { value.CompanyId, value.ClientId });
            entity.HasOne<Company>().WithMany().HasForeignKey(value => value.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Project>().WithMany().HasForeignKey(value => value.ProjectId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Client>().WithMany().HasForeignKey(value => value.ClientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Quote>().WithMany().HasForeignKey(value => value.RevisionOfId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ImportJob>().WithMany().HasForeignKey(value => value.ImportJobId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<QuoteItem>(entity =>
        {
            entity.ToTable("quote_items");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.LineType).HasMaxLength(10).IsRequired();
            entity.Property(value => value.ItemCode).HasMaxLength(50);
            entity.Property(value => value.Name).HasMaxLength(500).IsRequired();
            entity.Property(value => value.Spec).HasColumnType("text");
            entity.Property(value => value.Unit).HasMaxLength(20);
            entity.Property(value => value.Quantity).HasPrecision(18, 4);
            entity.Property(value => value.UnitPrice).HasPrecision(18, 4);
            entity.Property(value => value.DiscountRate).HasPrecision(5, 2);
            entity.Property(value => value.Amount).HasPrecision(18, 2);
            entity.Property(value => value.UnitCost).HasPrecision(18, 4);
            entity.Property(value => value.Remark).HasMaxLength(500);
            entity.HasIndex(value => new { value.CompanyId, value.QuoteId, value.SortOrder });
            entity.HasOne<Quote>().WithMany(value => value.Items).HasForeignKey(value => value.QuoteId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Product>().WithMany().HasForeignKey(value => value.ProductId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Code).HasMaxLength(50).IsRequired();
            entity.Property(value => value.Name).HasMaxLength(200).IsRequired();
            entity.Property(value => value.Spec).HasMaxLength(500);
            entity.Property(value => value.Unit).HasMaxLength(20);
            entity.Property(value => value.Category).HasMaxLength(100);
            entity.Property(value => value.DefaultPrice).HasPrecision(18, 4);
            entity.Property(value => value.DefaultCost).HasPrecision(18, 4);
            entity.HasIndex(value => new { value.CompanyId, value.Code }).IsUnique();
            entity.HasIndex(value => new { value.CompanyId, value.Name });
            entity.HasOne<Company>().WithMany().HasForeignKey(value => value.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ImportJob>(entity =>
        {
            entity.ToTable("import_jobs");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Kind).HasMaxLength(20).IsRequired();
            entity.Property(value => value.FileName).HasMaxLength(255).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.Property(value => value.PreviewJson).HasColumnType("longtext");
            entity.Property(value => value.ErrorsJson).HasColumnType("longtext");
            entity.HasIndex(value => new { value.CompanyId, value.Status, value.CreatedAt });
            entity.HasOne<Company>().WithMany().HasForeignKey(value => value.CompanyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ImportMappingProfile>().WithMany().HasForeignKey(value => value.MappingProfileId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<Project>().WithMany().HasForeignKey(value => value.TargetProjectId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ImportMappingProfile>(entity =>
        {
            entity.ToTable("import_mapping_profiles");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Name).HasMaxLength(100).IsRequired();
            entity.Property(value => value.SheetName).HasMaxLength(100);
            entity.Property(value => value.ColumnMapJson).HasColumnType("longtext");
            entity.Property(value => value.CellMapJson).HasColumnType("longtext");
            entity.HasIndex(value => new { value.CompanyId, value.Name }).IsUnique();
            entity.HasOne<Company>().WithMany().HasForeignKey(value => value.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>第二階段：客戶、專案與單號。</summary>
    private static void ConfigureBusiness(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Client>(entity =>
        {
            entity.ToTable("clients");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Code).HasMaxLength(50).IsRequired();
            entity.Property(value => value.Name).HasMaxLength(200).IsRequired();
            entity.Property(value => value.ShortName).HasMaxLength(100);
            entity.Property(value => value.TaxId).HasMaxLength(20);
            entity.Property(value => value.Industry).HasMaxLength(100);
            entity.Property(value => value.Phone).HasMaxLength(50);
            entity.Property(value => value.Fax).HasMaxLength(50);
            entity.Property(value => value.Email).HasMaxLength(200);
            entity.Property(value => value.Address).HasMaxLength(500);
            entity.Property(value => value.InvoiceTitle).HasMaxLength(200);
            entity.Property(value => value.InvoiceAddress).HasMaxLength(500);
            entity.Property(value => value.PaymentTerms).HasMaxLength(200);
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.Property(value => value.Note).HasColumnType("text");
            entity.HasIndex(value => new { value.CompanyId, value.Code }).IsUnique();
            entity.HasIndex(value => new { value.CompanyId, value.TaxId });
            entity.HasIndex(value => new { value.CompanyId, value.Name });
            entity.HasIndex(value => new { value.CompanyId, value.OwnerUserId });
            entity.HasIndex(value => new { value.CompanyId, value.DepartmentId });
            entity.HasOne<Department>().WithMany().HasForeignKey(value => value.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<Company>().WithMany().HasForeignKey(value => value.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Client>().WithMany().HasForeignKey(value => value.ParentClientId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<User>().WithMany().HasForeignKey(value => value.OwnerUserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ClientContact>(entity =>
        {
            entity.ToTable("client_contacts");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.Name).HasMaxLength(100).IsRequired();
            entity.Property(value => value.Title).HasMaxLength(100);
            entity.Property(value => value.Phone).HasMaxLength(50);
            entity.Property(value => value.Mobile).HasMaxLength(50);
            entity.Property(value => value.Email).HasMaxLength(200);
            entity.Property(value => value.Note).HasMaxLength(500);
            entity.HasIndex(value => new { value.CompanyId, value.ClientId });
            entity.HasOne<Client>().WithMany().HasForeignKey(value => value.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.ProjectNo).HasMaxLength(50).IsRequired();
            entity.Property(value => value.Name).HasMaxLength(200).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.Property(value => value.ExpectedAmount).HasPrecision(18, 2);
            entity.Property(value => value.ContractAmount).HasPrecision(18, 2);
            entity.Property(value => value.ContractTotal).HasPrecision(18, 2);
            entity.Property(value => value.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(value => value.Description).HasColumnType("text");
            entity.HasIndex(value => new { value.CompanyId, value.ProjectNo }).IsUnique();
            entity.HasIndex(value => new { value.CompanyId, value.Status, value.CreatedAt });
            entity.HasIndex(value => new { value.CompanyId, value.StartDate });
            entity.HasIndex(value => new { value.CompanyId, value.EndDate });
            entity.HasIndex(value => new { value.CompanyId, value.ExpectedAmount });
            entity.HasIndex(value => new { value.CompanyId, value.ContractAmount });
            entity.HasIndex(value => new { value.CompanyId, value.ClientId });
            entity.HasIndex(value => new { value.CompanyId, value.DepartmentId });
            entity.HasIndex(value => new { value.CompanyId, value.OwnerUserId });
            entity.HasOne<Company>().WithMany().HasForeignKey(value => value.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(value => value.Client).WithMany().HasForeignKey(value => value.ClientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Department>().WithMany().HasForeignKey(value => value.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(value => value.Owner).WithMany().HasForeignKey(value => value.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProjectMember>(entity =>
        {
            entity.ToTable("project_members");
            entity.HasKey(value => new { value.ProjectId, value.UserId });
            entity.Property(value => value.ProjectRole).HasMaxLength(20).IsRequired();
            entity.HasIndex(value => new { value.CompanyId, value.UserId });
            entity.HasOne<Project>().WithMany(value => value.Members).HasForeignKey(value => value.ProjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(value => value.User).WithMany().HasForeignKey(value => value.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProjectStatusHistory>(entity =>
        {
            entity.ToTable("project_status_histories");
            entity.HasKey(value => value.Id);
            entity.Property(value => value.FromStatus).HasMaxLength(20);
            entity.Property(value => value.ToStatus).HasMaxLength(20).IsRequired();
            entity.Property(value => value.Reason).HasMaxLength(500);
            entity.HasIndex(value => new { value.CompanyId, value.ProjectId, value.ChangedAt });
            entity.HasOne<Project>().WithMany().HasForeignKey(value => value.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NumberSequence>(entity =>
        {
            entity.ToTable("number_sequences");
            entity.HasKey(value => new { value.CompanyId, value.DocType, value.Period });
            entity.Property(value => value.DocType).HasMaxLength(20);
            entity.Property(value => value.Period).HasMaxLength(10);
            entity.Property(value => value.Prefix).HasMaxLength(20).IsRequired();
            entity.HasOne<Company>().WithMany().HasForeignKey(value => value.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceCompanyOwnership();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceCompanyOwnership();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// 新增時填入目前公司並拒絕其他公司的值；修改時禁止變更 CompanyId。
    /// </summary>
    /// <remarks>
    /// 沒有公司範圍的流程（登入、platform scope）可以寫入明確指定的 CompanyId，但不得留空；
    /// 這類流程只存在於 Identity 與平台開通，且都會寫 audit。
    /// </remarks>
    private void EnforceCompanyOwnership()
    {
        var currentCompanyId = CurrentCompanyId;
        foreach (EntityEntry<ICompanyOwned> entry in ChangeTracker.Entries<ICompanyOwned>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CompanyId == Guid.Empty)
                    {
                        entry.Entity.CompanyId = currentCompanyId ?? throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} 缺少公司範圍。");
                    }
                    else if (currentCompanyId is Guid companyId && entry.Entity.CompanyId != companyId)
                    {
                        throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} 不可寫入其他公司的資料。");
                    }
                    break;
                case EntityState.Modified when entry.Property(nameof(ICompanyOwned.CompanyId)).IsModified:
                    throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} 不可變更所屬公司。");
            }
        }
    }
}
