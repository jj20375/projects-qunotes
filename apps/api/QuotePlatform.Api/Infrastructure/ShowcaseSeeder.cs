using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuotePlatform.Api.Modules.Quotes;

namespace QuotePlatform.Api.Infrastructure;

/// <summary>
/// 對客戶展示用的公司「晴川系統整合」：近兩年的業務資料，涵蓋每一種專案狀態，
/// 以及一個從報價改版、訂單、分批出貨、分期發票、保固到跟進紀錄都走完的旗艦專案。
/// </summary>
/// <remarks>
/// 所有日期都從執行當天往回推算，所以重置（<c>--reset-demo-data</c>）之後 Dashboard 的今年、去年同期、
/// 逾期應收與到期保固永遠是「現在」的樣子。直接寫入 entity 而不是走 API，是因為 API 只能產生今天的時間戳記，
/// 做不出跨月份的業績趨勢。金額仍由 <see cref="QuoteCalculator"/> 計算，單號由 <see cref="NumberSequenceService"/> 取號。
/// smoke 測試不使用這家公司的帳號，因此不會被測試資料污染。
/// </remarks>
public sealed class ShowcaseSeeder(QuoteDbContext db, CompanyProvisioner provisioner, CompanyContext companyContext, NumberSequenceService numbers, IOptions<QuoteOptions> options, ILogger<ShowcaseSeeder> logger)
{
    /// <summary>展示公司的代碼（companies.Slug）。</summary>
    public const string Slug = "showcase";

    private static readonly (string Key, string Account, string Name, string Department, string Role)[] People =
    [
        ("manager", "sc-manager", "王雅婷", "sales", "sales-manager"),
        ("amy", "sc-amy", "林小美", "sales1", "sales"),
        ("kevin", "sc-kevin", "張志明", "sales1", "sales"),
        ("david", "sc-david", "陳大文", "sales2", "sales"),
        ("finance", "sc-finance", "黃淑芬", "fin", "accountant"),
        ("engineer", "sc-engineer", "吳俊傑", "eng", "viewer"),
    ];

    private readonly DateOnly today = BusinessTime.Today;
    private readonly Dictionary<string, Guid> users = [];
    private readonly Dictionary<string, Guid> userDepartments = [];
    private readonly Dictionary<string, Guid> departments = [];
    private readonly Dictionary<string, Product> products = [];
    private readonly Dictionary<string, Client> clients = [];
    private readonly Dictionary<Guid, List<ClientContact>> contacts = [];
    private Guid companyId;
    private CompanySetting setting = null!;
    private int invoiceSerial;
    private int warrantySerial;

    /// <summary>展示公司不存在時建立；已存在就不動（要重來請用 --reset-demo-data）。</summary>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.Companies.AnyAsync(value => value.Slug == Slug, cancellationToken)) return;
        var password = options.Value.BootstrapAdminPassword;
        if (PasswordRules.Validate(password) is not null) return;
        var accounts = People.Select(value => value.Account).Append("sc-ceo").ToList();
        if (await db.Users.AnyAsync(value => accounts.Contains(value.Account), cancellationToken))
        {
            logger.LogWarning("未建立展示公司：sc- 開頭的帳號已存在，請先執行 --reset-demo-data。");
            return;
        }

        var (company, adminMembership) = await provisioner.CreateAsync(
            new CompanyOnboarding("晴川系統整合股份有限公司", Slug, null, "02-2755-6800", "台北市大安區敦化南路二段 76 號 9 樓", "sc-ceo", "周家豪", "ceo@qingchuan.example", password),
            cancellationToken);
        companyId = company.Id;
        companyContext.UseCompany(company.Id);
        company.EnglishName = "Qingchuan Systems Integration Co., Ltd.";
        users["ceo"] = adminMembership.UserId;
        userDepartments["ceo"] = adminMembership.DepartmentId!.Value;

        setting = await db.CompanySettings.SingleAsync(cancellationToken);
        setting.DefaultTerms = QuoteTerms.Normalize("""
            1. 本報價有效期限 30 天，逾期請重新詢價。
            2. 付款條件依合約約定；分期付款者依驗收進度請款。
            3. 保固期間自驗收日起算，人為損壞與天災不在保固範圍。
            4. 施工期間如需停機，由雙方另行協調時段。
            """);

        await SeedOrganizationAsync(password, cancellationToken);
        SeedProducts();
        await SeedClientsAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var deal in Deals().OrderBy(value => value.Created)) await AddDealAsync(deal, cancellationToken);
        logger.LogInformation("已建立展示公司 {Slug}。", Slug);
    }

    private async Task SeedOrganizationAsync(string password, CancellationToken cancellationToken)
    {
        var root = await db.Departments.SingleAsync(value => value.ParentId == null, cancellationToken);
        var sales = await DepartmentTree.AddAsync(db, companyId, root.Id, "業務部", "SALES", 10, cancellationToken);
        // 子部門的 closure 要查得到上層的 closure，所以每一層都先存檔。
        await db.SaveChangesAsync(cancellationToken);
        departments["sales"] = sales.Id;
        departments["sales1"] = (await DepartmentTree.AddAsync(db, companyId, sales.Id, "業務一課", "SALES-1", 10, cancellationToken)).Id;
        departments["sales2"] = (await DepartmentTree.AddAsync(db, companyId, sales.Id, "業務二課", "SALES-2", 20, cancellationToken)).Id;
        departments["eng"] = (await DepartmentTree.AddAsync(db, companyId, root.Id, "工程部", "ENG", 20, cancellationToken)).Id;
        departments["fin"] = (await DepartmentTree.AddAsync(db, companyId, root.Id, "財務部", "FIN", 30, cancellationToken)).Id;
        await db.SaveChangesAsync(cancellationToken);

        var roles = await db.Roles.Where(value => value.CompanyId == null).ToDictionaryAsync(value => value.Code, cancellationToken);
        foreach (var (key, account, name, department, role) in People)
        {
            var user = new User { Account = account, DisplayName = name, Email = $"{key}@qingchuan.example", CreatedAt = At(M(22, 1), 9) };
            user.PasswordHash = PasswordRules.Hash(user, password);
            db.Users.Add(user);
            db.CompanyMemberships.Add(new CompanyMembership { CompanyId = companyId, UserId = user.Id, DepartmentId = departments[department], RoleId = roles[role].Id });
            users[key] = user.Id;
            userDepartments[key] = departments[department];
        }
    }

    private void SeedProducts()
    {
        (string Code, string Name, string Spec, string Unit, string Category, decimal Price, decimal Cost)[] rows =
        [
            ("NW-SW24", "PoE 網路交換器 24 埠", "24×GbE PoE+、4×SFP+", "台", "網路設備", 26000, 18500),
            ("NW-SW48", "PoE 網路交換器 48 埠", "48×GbE PoE+、4×SFP+", "台", "網路設備", 38000, 26000),
            ("NW-AP6", "企業級無線基地台", "Wi-Fi 6、PoE 供電", "台", "網路設備", 9800, 6200),
            ("NW-FW1", "次世代防火牆", "1 Gbps 吞吐量、含一年授權", "台", "資安", 68000, 49000),
            ("SV-R740", "機架式伺服器", "2U、雙 CPU、128GB RAM", "台", "伺服器", 185000, 142000),
            ("ST-NAS8", "NAS 儲存設備 8Bay", "8×8TB、RAID 6", "台", "儲存", 96000, 70000),
            ("CM-IP4K", "4K 網路攝影機", "800 萬畫素、夜視 30m", "支", "監控", 7600, 4800),
            ("CM-NVR32", "32 路網路錄影主機", "含 4×8TB 硬碟", "台", "監控", 52000, 36000),
            ("CB-CAT6", "Cat.6 網路佈線", "含資訊插座與測試報告", "點", "施工", 1800, 900),
            ("SV-INST", "安裝與設定服務", "現場安裝、設定與教育訓練", "式", "服務", 25000, 10000),
            ("SV-MA1Y", "年度維護合約", "5×8 到場、每季保養", "年", "維護", 60000, 30000),
            ("SV-MOVE", "機房搬遷服務", "含拆裝、搬運與上架", "式", "服務", 45000, 22000),
        ];
        foreach (var row in rows)
        {
            var product = new Product { Code = row.Code, Name = row.Name, Spec = row.Spec, Unit = row.Unit, Category = row.Category, DefaultPrice = row.Price, DefaultCost = row.Cost, CreatedAt = At(M(22, 2), 10) };
            db.Products.Add(product);
            products[row.Code] = product;
        }
    }

    private async Task SeedClientsAsync(CancellationToken cancellationToken)
    {
        (string Short, string Name, string Industry, string Phone, string Address, string Owner, string Terms, (string Name, string Title, string Phone, string Email)[] People)[] rows =
        [
            ("瑞峰精密", "瑞峰精密工業股份有限公司", "製造業", "03-328-1100", "桃園市龜山區文化一路 88 號", "amy", "月結 60 天",
                [("陳經理", "資訊課長", "03-328-1100#201", "chen@ruifeng.example"), ("李工程師", "網管", "03-328-1100#215", "lee@ruifeng.example")]),
            ("晨光生技", "晨光生技股份有限公司", "生技醫藥", "03-578-6600", "新竹市東區光復路一段 89 號", "amy", "月結 30 天",
                [("吳協理", "營運協理", "03-578-6600#100", "wu@morninglight.example")]),
            ("北辰物流", "北辰物流股份有限公司", "物流倉儲", "04-2359-8800", "台中市西屯區工業區一路 2 號", "david", "月結 45 天",
                [("許主任", "總務主任", "04-2359-8800#12", "hsu@beichen.example"), ("蔡專員", "採購", "04-2359-8800#31", "tsai@beichen.example")]),
            ("青峰醫院", "青峰醫療社團法人青峰醫院", "醫療", "07-338-1122", "高雄市前鎮區中山二路 5 號", "david", "驗收後 60 天",
                [("鄭組長", "資訊組長", "07-338-1122#3301", "cheng@qingfeng.example")]),
            ("安和建設", "安和建設股份有限公司", "營建", "02-2700-5566", "台北市大安區安和路二段 70 號", "kevin", "月結 30 天",
                [("林副理", "工務部副理", "02-2700-5566#88", "lin@anhe.example")]),
            ("海悅酒店", "海悅國際酒店股份有限公司", "觀光旅宿", "02-2311-8899", "台北市中正區忠孝西路一段 50 號", "kevin", "月結 30 天",
                [("黃經理", "資訊部經理", "02-2311-8899#601", "huang@haiyue.example"), ("劉主任", "工程部主任", "02-2311-8899#620", "liu@haiyue.example")]),
            ("宏信會計", "宏信聯合會計師事務所", "專業服務", "02-2545-7788", "台北市松山區南京東路三段 210 號", "david", "月結 30 天",
                [("蘇會計師", "所長", "02-2545-7788#1", "su@hongxin.example")]),
            ("明德科大", "明德科技大學", "教育", "05-271-3300", "嘉義縣民雄鄉建國路 1 號", "david", "驗收後 30 天",
                [("何組長", "圖資處網路組組長", "05-271-3300#2201", "ho@mingde.example"), ("江技士", "圖資處技士", "05-271-3300#2205", "chiang@mingde.example")]),
        ];
        foreach (var row in rows)
        {
            var client = new Client
            {
                Code = await numbers.NextClientCodeAsync(companyId, cancellationToken),
                Name = row.Name,
                ShortName = row.Short,
                Industry = row.Industry,
                Phone = row.Phone,
                Address = row.Address,
                InvoiceTitle = row.Name,
                InvoiceAddress = row.Address,
                PaymentTerms = row.Terms,
                OwnerUserId = users[row.Owner],
                DepartmentId = userDepartments[row.Owner],
                CreatedBy = users["ceo"],
                CreatedAt = At(M(22, 3), 11),
            };
            db.Clients.Add(client);
            var list = row.People.Select((person, index) => new ClientContact
            {
                ClientId = client.Id,
                Name = person.Name,
                Title = person.Title,
                Phone = person.Phone,
                Email = person.Email,
                IsPrimary = index == 0,
                SortOrder = index,
            }).ToList();
            db.ClientContacts.AddRange(list);
            clients[row.Short] = client;
            contacts[client.Id] = list;
        }
    }

    private async Task AddDealAsync(Deal deal, CancellationToken cancellationToken)
    {
        var client = clients[deal.Client];
        var owner = users[deal.Owner];
        var created = Clip(deal.Created);
        var year = created.Year.ToString();
        var project = new Project
        {
            ProjectNo = await numbers.NextAsync(companyId, "project", $"P{year}-", year, 4, cancellationToken),
            Name = deal.Name,
            ClientId = client.Id,
            DepartmentId = userDepartments[deal.Owner],
            OwnerUserId = owner,
            ExpectedAmount = deal.Expected,
            Description = deal.Description,
            CreatedBy = owner,
            CreatedAt = At(created, 10),
        };
        db.Projects.Add(project);
        db.ProjectStatusHistories.Add(new ProjectStatusHistory { ProjectId = project.Id, ToStatus = ProjectStatuses.Draft, ChangedBy = owner, ChangedAt = project.CreatedAt });
        foreach (var (member, role) in deal.Members)
        {
            db.ProjectMembers.Add(new ProjectMember { ProjectId = project.Id, UserId = users[member], CompanyId = companyId, ProjectRole = role, AddedBy = owner, AddedAt = At(created, 11) });
        }

        if (deal.QuoteDate is DateOnly quoteOn)
        {
            var quoteDate = Clip(quoteOn);
            Quote quote;
            if (deal.RevisedFrom is DateOnly firstOn)
            {
                // 旗艦專案：R0 原價送出，議價後改版為 R1（打折），R0 被取代。
                var first = await NewQuoteAsync(project, client, owner, deal, Clip(firstOn), discount: 0, cancellationToken);
                Advance(first, QuoteStatuses.Sent, null, owner);
                first.Status = QuoteStatuses.Superseded;
                first.IsLatest = false;
                ChangeStatus(project, ProjectStatuses.Quoting, $"建立報價單 {first.QuoteNo}", first.CreatedAt, owner);
                quote = BuildQuote(project, client, owner, deal, quoteDate, deal.DiscountPercent);
                quote.QuoteNo = first.QuoteNo;
                quote.RevisionNo = 1;
                quote.RevisionOfId = first.Id;
                quote.Source = QuoteSources.Copy;
                db.Quotes.Add(quote);
            }
            else
            {
                quote = await NewQuoteAsync(project, client, owner, deal, quoteDate, deal.DiscountPercent, cancellationToken);
                ChangeStatus(project, ProjectStatuses.Quoting, $"建立報價單 {quote.QuoteNo}", quote.CreatedAt, owner);
            }
            var decidedOn = deal.DecidedOn is DateOnly decided ? Clip(decided) : (DateOnly?)null;
            Advance(quote, deal.QuoteStatus, decidedOn, owner, deal.Reason);

            if (quote.Status == QuoteStatuses.Accepted)
            {
                project.ContractAmount = quote.NetAmount;
                project.ContractTotal = quote.Total;
                project.WonAt = quote.AcceptedAt;
                ChangeStatus(project, ProjectStatuses.Won, $"客戶接受報價 {quote.QuoteNo}-R{quote.RevisionNo}", quote.AcceptedAt!.Value, owner);
                AddFulfilment(deal, project, client, quote, owner);
            }
            else if (quote.Status == QuoteStatuses.Rejected)
            {
                ChangeStatus(project, ProjectStatuses.Lost, deal.Reason, quote.RejectedAt!.Value.AddHours(2), owner);
            }
        }
        if (deal.Status == ProjectStatuses.Cancelled && deal.EndedOn is DateOnly endedOn)
        {
            ChangeStatus(project, ProjectStatuses.Cancelled, deal.Reason, At(Clip(endedOn), 15), owner);
        }

        AddFollowUps(deal, project, client);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>訂單、出貨、發票、保固與後續的專案狀態（執行中、已完工、已結案）。</summary>
    private void AddFulfilment(Deal deal, Project project, Client client, Quote quote, Guid owner)
    {
        var acceptedOn = DateOnly.FromDateTime(quote.AcceptedAt!.Value.ToOffset(BusinessTime.Offset).DateTime);
        var orderDate = Clip(acceptedOn.AddDays(deal.OrderAfterDays));
        var order = new ProjectOrder
        {
            ProjectId = project.Id,
            QuoteId = quote.Id,
            OrderNo = $"PO-{client.Code}-{orderDate:yyMMdd}",
            OrderDate = orderDate,
            Amount = quote.NetAmount,
            TaxAmount = quote.TaxAmount,
            Total = quote.Total,
            Status = deal.Status switch
            {
                ProjectStatuses.Won => "received",
                ProjectStatuses.InProgress => "in_progress",
                _ => "delivered",
            },
            Note = deal.OrderNote,
            CreatedBy = owner,
            CreatedAt = At(orderDate, 16),
        };
        db.ProjectOrders.Add(order);
        project.StartDate = orderDate;
        project.EndDate = orderDate.AddDays(deal.DurationDays);

        var stages = deal.Status switch
        {
            ProjectStatuses.InProgress => 1,
            ProjectStatuses.Completed => 2,
            ProjectStatuses.Closed => 3,
            _ => 0,
        };
        var startedOn = Clip(orderDate.AddDays(3));
        var completedOn = Clip(orderDate.AddDays(deal.DurationDays));
        if (stages >= 1) ChangeStatus(project, ProjectStatuses.InProgress, "設備進場，開始施工", At(startedOn, 9), owner);
        if (stages >= 2) ChangeStatus(project, ProjectStatuses.Completed, "完工驗收", At(completedOn, 17), owner);
        if (stages >= 3)
        {
            var closedOn = Clip(completedOn.AddDays(30));
            project.ClosedAt = At(closedOn, 17);
            ChangeStatus(project, ProjectStatuses.Closed, "款項收齊，結案", project.ClosedAt.Value, owner);
        }

        var contactList = contacts[client.Id];
        var shipmentIndex = 0;
        foreach (var ship in deal.Shipments)
        {
            var shipDate = orderDate.AddDays(ship.AfterOrderDays);
            if (shipDate > today) continue;
            var receiver = contactList.ElementAtOrDefault(ship.ContactIndex) ?? contactList.FirstOrDefault();
            db.ProjectShipments.Add(new ProjectShipment
            {
                ProjectId = project.Id,
                OrderId = order.Id,
                ShipmentNo = $"SH-{shipDate:yyyyMMdd}-{++shipmentIndex}",
                ShipDate = shipDate,
                Status = ship.Status,
                ReceiverName = receiver?.Name,
                DeliveryAddress = client.Address,
                TrackingNo = $"HCT{shipDate:yyMMdd}{shipmentIndex:D4}",
                Note = ship.Note,
                CreatedBy = owner,
                CreatedAt = At(shipDate, 10),
            });
        }

        var billed = 0m;
        for (var index = 0; index < deal.Invoices.Length; index++)
        {
            var bill = deal.Invoices[index];
            var invoiceDate = orderDate.AddDays(bill.AfterOrderDays);
            if (invoiceDate > today) continue;
            // 最後一期用剩餘金額，避免四捨五入後各期加總和訂單差 1 元。
            var sales = index == deal.Invoices.Length - 1 ? order.Amount - billed : QuoteCalculator.Round(order.Amount * bill.Ratio, 0);
            billed += sales;
            var tax = QuoteCalculator.Round(sales * setting.DefaultTaxRate / 100m, 0);
            var paidOn = Clip(invoiceDate.AddDays(Math.Min(bill.DueDays, 25)));
            db.ProjectInvoices.Add(new ProjectInvoice
            {
                ProjectId = project.Id,
                OrderId = order.Id,
                InvoiceNo = $"QC-{51830000 + ++invoiceSerial:D8}",
                InvoiceDate = invoiceDate,
                BuyerName = client.InvoiceTitle ?? client.Name,
                SalesAmount = sales,
                TaxAmount = tax,
                TotalAmount = sales + tax,
                PaidAmount = bill.Paid ? sales + tax : 0,
                PaymentStatus = bill.Paid ? "paid" : "unpaid",
                PaidAt = bill.Paid ? paidOn : null,
                DueDate = invoiceDate.AddDays(bill.DueDays),
                Note = bill.Note,
                CreatedBy = users["finance"],
                CreatedAt = At(invoiceDate, 16),
            });
        }

        if (deal.WarrantyMonths > 0)
        {
            var start = orderDate.AddDays(deal.WarrantyStartAfterOrderDays);
            if (start <= today)
            {
                db.ProjectWarranties.Add(new ProjectWarranty
                {
                    ProjectId = project.Id,
                    OrderId = order.Id,
                    WarrantyNo = $"WR-{start:yyyyMM}-{++warrantySerial:D2}",
                    Subject = deal.WarrantySubject ?? project.Name,
                    StartDate = start,
                    EndDate = start.AddMonths(deal.WarrantyMonths).AddDays(-1),
                    Terms = $"硬體保固 {deal.WarrantyMonths} 個月，故障 4 小時內回應、次工作日到場。",
                    CreatedBy = owner,
                    CreatedAt = At(start, 15),
                });
            }
        }
    }

    private void AddFollowUps(Deal deal, Project project, Client client)
    {
        ProjectFollowUp? latest = null;
        foreach (var visit in deal.FollowUps)
        {
            var contact = contacts[client.Id].ElementAtOrDefault(visit.ContactIndex);
            var occurredAt = At(Clip(visit.On), visit.Hour);
            var row = new ProjectFollowUp
            {
                ProjectId = project.Id,
                Type = visit.Type,
                OccurredAt = occurredAt,
                ContactId = contact?.Id,
                ContactName = contact?.Name,
                Summary = visit.Summary,
                Content = visit.Content,
                NextFollowUpOn = visit.Next,
                NextAction = visit.Next is null ? null : visit.NextAction,
                CreatedBy = users[visit.By],
                CreatedAt = occurredAt,
                UpdatedAt = occurredAt,
            };
            db.ProjectFollowUps.Add(row);
            if (latest is null || row.OccurredAt > latest.OccurredAt) latest = row;
        }
        // 專案上的跟進快取規則與 FollowUpService 相同：最新一筆的發生時間與下次聯絡日。
        project.LastFollowUpAt = latest?.OccurredAt;
        project.NextFollowUpOn = latest?.NextFollowUpOn;
    }

    private async Task<Quote> NewQuoteAsync(Project project, Client client, Guid owner, Deal deal, DateOnly quoteDate, decimal discount, CancellationToken cancellationToken)
    {
        var quote = BuildQuote(project, client, owner, deal, quoteDate, discount);
        quote.QuoteNo = await numbers.NextByPatternAsync(
            companyId,
            "quote",
            setting.QuoteNumberPattern,
            quoteDate,
            candidate => db.Quotes.IgnoreQueryFilters().AnyAsync(value => value.CompanyId == companyId && value.QuoteNo == candidate, cancellationToken),
            cancellationToken);
        db.Quotes.Add(quote);
        return quote;
    }

    private Quote BuildQuote(Project project, Client client, Guid owner, Deal deal, DateOnly quoteDate, decimal discount)
    {
        var contact = contacts[client.Id].FirstOrDefault();
        var quote = new Quote
        {
            ProjectId = project.Id,
            ClientId = client.Id,
            Title = deal.QuoteTitle ?? deal.Name,
            QuoteDate = quoteDate,
            ValidUntil = quoteDate.AddDays(setting.QuoteValidDays),
            ClientName = client.InvoiceTitle ?? client.Name,
            ClientAddress = client.InvoiceAddress ?? client.Address,
            ContactName = contact?.Name,
            ContactPhone = contact?.Phone ?? client.Phone,
            ContactEmail = contact?.Email,
            Currency = setting.DefaultCurrency,
            TaxRate = setting.DefaultTaxRate,
            DiscountType = discount > 0 ? QuoteDiscountTypes.Percent : QuoteDiscountTypes.None,
            DiscountValue = discount,
            PaymentTerms = deal.PaymentTerms ?? client.PaymentTerms,
            DeliveryTerms = "簽約後 30 天內交貨並完成安裝",
            Terms = setting.DefaultTerms,
            CreatedBy = owner,
            CreatedAt = At(quoteDate, 14),
            UpdatedBy = owner,
            UpdatedAt = At(quoteDate, 14),
        };
        var sort = 0;
        foreach (var (code, quantity) in deal.Lines)
        {
            var product = products[code];
            quote.Items.Add(new QuoteItem
            {
                QuoteId = quote.Id,
                SortOrder = ++sort,
                LineType = QuoteLineTypes.Item,
                ProductId = product.Id,
                ItemCode = product.Code,
                Name = product.Name,
                Spec = product.Spec,
                Unit = product.Unit,
                Quantity = quantity,
                UnitPrice = product.DefaultPrice,
                UnitCost = product.DefaultCost,
            });
        }
        QuoteCalculator.Apply(quote);
        return quote;
    }

    /// <summary>把報價推進到指定狀態，補上沿途每一關的時間與經手人（核准一律由業務主管）。</summary>
    private void Advance(Quote quote, string target, DateOnly? decidedOn, Guid owner, string? reason = null)
    {
        string[] order = [QuoteStatuses.Draft, QuoteStatuses.PendingApproval, QuoteStatuses.Approved, QuoteStatuses.Sent];
        var level = target is QuoteStatuses.Accepted or QuoteStatuses.Rejected ? order.Length : Array.IndexOf(order, target) + 1;
        var day = quote.QuoteDate;
        if (level >= 2) { quote.SubmittedBy = owner; quote.SubmittedAt = At(day, 15); }
        if (level >= 3) { quote.ApprovedBy = users["manager"]; quote.ApprovedAt = At(day, 17); }
        if (level >= 4) quote.SentAt = At(Clip(day.AddDays(1)), 10);
        var decided = At(decidedOn ?? Clip(day.AddDays(7)), 11);
        if (target == QuoteStatuses.Accepted) quote.AcceptedAt = decided;
        if (target == QuoteStatuses.Rejected) { quote.RejectedAt = decided; quote.StatusReason = reason; }
        quote.Status = target;
        quote.UpdatedAt = quote.AcceptedAt ?? quote.RejectedAt ?? quote.SentAt ?? quote.ApprovedAt ?? quote.SubmittedAt ?? quote.CreatedAt;
    }

    private void ChangeStatus(Project project, string status, string? reason, DateTimeOffset at, Guid userId)
    {
        db.ProjectStatusHistories.Add(new ProjectStatusHistory { ProjectId = project.Id, FromStatus = project.Status, ToStatus = status, Reason = reason, ChangedBy = userId, ChangedAt = at });
        project.Status = status;
        project.UpdatedBy = userId;
        project.UpdatedAt = at;
    }

    /// <summary>距今 <paramref name="monthsAgo"/> 個月的那個月第 <paramref name="day"/> 天。</summary>
    private DateOnly M(int monthsAgo, int day) => new DateOnly(today.Year, today.Month, 1).AddMonths(-monthsAgo).AddDays(day - 1);

    private DateOnly T(int days) => today.AddDays(days);

    // 在月初執行時，「本月第 20 天」會落在未來；一律壓回今天，資料才不會出現未來發生的事。
    private DateOnly Clip(DateOnly date) => date > today ? today : date;

    private static DateTimeOffset At(DateOnly date, int hour) => new(date.ToDateTime(new TimeOnly(hour, 0)), BusinessTime.Offset);

    private static (string, decimal) L(string code, decimal quantity) => (code, quantity);

    private IEnumerable<Deal> Deals()
    {
        const string accepted = QuoteStatuses.Accepted;
        yield return new("一廠網路骨幹升級", "瑞峰精密", "amy", M(21, 6), ProjectStatuses.Closed, 600000, [L("NW-SW48", 6), L("NW-FW1", 1), L("CB-CAT6", 120), L("SV-INST", 1)])
        { QuoteDate = M(20, 3), QuoteStatus = accepted, DecidedOn = M(20, 24), Invoices = [new(1m, 20, 60, true)], WarrantyMonths = 12, WarrantySubject = "核心交換器與防火牆" };
        yield return new("門診區監控系統", "青峰醫院", "david", M(19, 10), ProjectStatuses.Closed, 420000, [L("CM-IP4K", 32), L("CM-NVR32", 1), L("CB-CAT6", 32), L("SV-INST", 1)])
        { QuoteDate = M(18, 2), QuoteStatus = accepted, DecidedOn = M(18, 15), Invoices = [new(0.3m, 5, 30, true, "訂金 30%"), new(0.7m, 40, 60, true, "驗收款 70%")], WarrantyMonths = 18, WarrantyStartAfterOrderDays = 25, WarrantySubject = "攝影機與錄影主機" };
        yield return new("倉儲 Wi-Fi 建置", "北辰物流", "david", M(16, 5), ProjectStatuses.Closed, 380000, [L("NW-AP6", 20), L("NW-SW24", 2), L("CB-CAT6", 40), L("SV-INST", 1)])
        { QuoteDate = M(15, 1), QuoteStatus = accepted, DecidedOn = M(15, 12), Invoices = [new(1m, 15, 45, true)], WarrantyMonths = 15, WarrantySubject = "無線基地台與交換器" };
        yield return new("實驗室 NAS 備份", "晨光生技", "amy", M(14, 8), ProjectStatuses.Completed, 300000, [L("ST-NAS8", 2), L("SV-INST", 1), L("SV-MA1Y", 1)])
        { QuoteDate = M(13, 6), QuoteStatus = accepted, DecidedOn = M(13, 20), Invoices = [new(1m, 10, 30, true)], WarrantyMonths = 12, WarrantyStartAfterOrderDays = 18, WarrantySubject = "NAS 儲存設備" };
        yield return new("客房網路汰換", "海悅酒店", "kevin", M(12, 3), ProjectStatuses.Closed, 1000000, [L("NW-SW48", 8), L("NW-AP6", 60), L("SV-INST", 2)])
        { QuoteDate = M(11, 1), QuoteStatus = accepted, DecidedOn = M(11, 5), DiscountPercent = 3, Invoices = [new(0.5m, 5, 30, true, "第一期 50%"), new(0.5m, 45, 30, true, "第二期 50%")], WarrantyMonths = 24, WarrantySubject = "客房無線網路設備" };
        yield return new("辦公室資安強化", "宏信會計", "david", M(10, 2), ProjectStatuses.Completed, 200000, [L("NW-FW1", 1), L("NW-SW24", 1), L("SV-INST", 1), L("SV-MA1Y", 1)])
        { QuoteDate = M(10, 6), QuoteStatus = accepted, DecidedOn = M(10, 14), Invoices = [new(1m, 10, 30, true)], WarrantyMonths = 12, WarrantySubject = "防火牆與交換器" };
        yield return new("圖書館無線網路", "明德科大", "david", M(10, 20), ProjectStatuses.Completed, 750000, [L("NW-AP6", 45), L("NW-SW48", 3), L("CB-CAT6", 60), L("SV-INST", 2)])
        { QuoteDate = M(9, 3), QuoteStatus = accepted, DecidedOn = M(9, 25), PaymentTerms = "驗收後 30 天", Invoices = [new(0.5m, 10, 60, true), new(0.5m, 50, 60, true)], WarrantyMonths = 36, WarrantySubject = "無線網路設備（教育優惠三年保固）" };
        yield return new("總部機房建置", "安和建設", "kevin", M(8, 12), ProjectStatuses.Completed, 650000, [L("SV-R740", 2), L("ST-NAS8", 1), L("NW-SW48", 2), L("SV-INST", 2)])
        { QuoteDate = M(7, 2), QuoteStatus = accepted, DecidedOn = M(7, 16), Invoices = [new(1m, 20, 30, true)], WarrantyMonths = 12, WarrantySubject = "伺服器與儲存設備" };
        yield return new("台南轉運站監控", "北辰物流", "david", M(6, 10), ProjectStatuses.InProgress, 320000, [L("CM-IP4K", 24), L("CM-NVR32", 1), L("CB-CAT6", 24), L("SV-INST", 1)])
        {
            QuoteDate = M(5, 1), QuoteStatus = accepted, DecidedOn = M(5, 9), DurationDays = 150, OrderNote = "分區施工，配合轉運站營運時段",
            Invoices = [new(0.3m, 3, 30, true, "訂金 30%"), new(0.7m, 95, 30, false, "第一、二區完工款")],
            Members = [("engineer", ProjectRoles.Viewer)],
            FollowUps =
            [
                new(T(-12), 10, "david", FollowUpTypes.Call, "催收第一、二區完工款", "許主任表示請款單已送財務，月底前付款。", 0, T(-5), "確認是否已匯款"),
                new(T(-5), 15, "david", FollowUpTypes.Email, "再次提醒逾期款項", "寄送對帳單給蔡專員。", 1, T(2), "電話確認付款日"),
            ],
        };
        yield return new("二廠新廠區網路建置", "瑞峰精密", "amy", M(3, 15), ProjectStatuses.InProgress, 1300000, [L("NW-SW48", 12), L("NW-AP6", 24), L("NW-FW1", 1), L("CB-CAT6", 240), L("SV-INST", 2)])
        {
            Description = "二廠新建廠房三至五樓的網路骨幹、無線網路與資安設備，分兩批進場。",
            QuoteTitle = "二廠新廠區網路設備與佈線",
            RevisedFrom = M(2, 2), QuoteDate = M(2, 18), QuoteStatus = accepted, DecidedOn = M(2, 28), DiscountPercent = 5, OrderAfterDays = 5, DurationDays = 70,
            PaymentTerms = "訂金 30%、第一批到貨 40%、驗收 30%", OrderNote = "分兩批出貨：交換器與防火牆、無線基地台",
            Shipments = [new(17, "delivered", 0, "第一批：交換器與防火牆"), new(32, "shipped", 1, "第二批：無線基地台")],
            Invoices = [new(0.3m, 2, 30, true, "訂金 30%"), new(0.4m, 18, 45, false, "第一批到貨 40%"), new(0.3m, 75, 30, false, "驗收款 30%")],
            WarrantyMonths = 12, WarrantyStartAfterOrderDays = 17, WarrantySubject = "網路交換器與防火牆",
            Members = [("manager", ProjectRoles.Manager), ("engineer", ProjectRoles.Viewer)],
            FollowUps =
            [
                new(M(3, 16), 10, "amy", FollowUpTypes.Visit, "現場勘查二廠新廠區", "確認三、五樓機櫃位置；客戶希望追加 40 個佈線點。", 0, M(3, 25), "提供初版規劃與報價"),
                new(M(2, 2), 16, "amy", FollowUpTypes.Email, "寄送初版報價 R0", "依勘查結果報價，等待客戶內部簽核。", 0, M(2, 9), "確認預算與付款方式"),
                new(M(2, 15), 14, "manager", FollowUpTypes.Meeting, "主管陪同議價", "客戶希望整體降價；同意以 95 折改版報價，付款改為三期。", 0, M(2, 20), "寄送 R1 報價"),
                new(M(1, 20), 11, "amy", FollowUpTypes.Visit, "第一批設備到貨驗收", "交換器與防火牆已上架，客戶簽收出貨單。", 1, T(-1), "追蹤第二批到貨時間"),
                new(T(-1), 9, "amy", FollowUpTypes.Call, "確認第二批到貨時間", "物流顯示已出貨，預計後天送達二廠。", 1, T(3), "第二批到貨驗收"),
            ],
        };
        yield return new("會議室影音與網路", "海悅酒店", "kevin", M(2, 5), ProjectStatuses.Won, 220000, [L("NW-SW24", 2), L("NW-AP6", 12), L("SV-INST", 1)])
        {
            QuoteDate = M(1, 2), QuoteStatus = accepted, DecidedOn = M(1, 15), OrderAfterDays = 5,
            FollowUps = [new(T(-4), 15, "kevin", FollowUpTypes.Call, "確認進場施工日期", "黃經理希望避開週末婚宴檔期。", 0, T(5), "排定施工時段")],
        };
        yield return new("急診資訊機房擴充", "青峰醫院", "david", M(1, 10), ProjectStatuses.Won, 600000, [L("SV-R740", 2), L("ST-NAS8", 1), L("SV-INST", 1), L("SV-MA1Y", 1)])
        {
            QuoteDate = M(1, 20), QuoteStatus = accepted, DecidedOn = T(-3), OrderAfterDays = 1,
            FollowUps = [new(T(-3), 14, "david", FollowUpTypes.Meeting, "院方簽回報價", "鄭組長確認預算已核定，下週安排上線時段。", 0, T(7), "與資訊組確認停機時段")],
        };
        yield return new("研發大樓網路規劃", "晨光生技", "amy", M(1, 8), ProjectStatuses.Quoting, 700000, [L("NW-SW48", 4), L("NW-AP6", 30), L("CB-CAT6", 80), L("SV-INST", 2)])
        {
            QuoteDate = M(1, 28), QuoteStatus = QuoteStatuses.Sent,
            FollowUps =
            [
                new(M(1, 29), 10, "amy", FollowUpTypes.Email, "寄送報價", null, 0, T(-6), "電話確認收到報價"),
                new(T(-5), 16, "amy", FollowUpTypes.Call, "吳協理表示內部評估中", "競爭對手也有報價，客戶在意保固年限。", 0, T(-2), "提供延長保固方案"),
            ],
        };
        yield return new("宿舍網路升級", "明德科大", "david", M(1, 18), ProjectStatuses.Quoting, 1300000, [L("NW-SW48", 10), L("NW-AP6", 80), L("SV-INST", 3)])
        {
            QuoteDate = T(-2), QuoteStatus = QuoteStatuses.PendingApproval, PaymentTerms = "驗收後 30 天",
            FollowUps = [new(T(-3), 10, "david", FollowUpTypes.Visit, "現場勘查學生宿舍", "共 4 棟、每層 2 台基地台；預計寒假施工。", 0, T(4), "主管核准後寄出報價")],
        };
        yield return new("新建案工地網路", "安和建設", "kevin", T(-3), ProjectStatuses.Quoting, 280000, [L("NW-SW24", 3), L("NW-AP6", 10), L("NW-FW1", 1), L("SV-INST", 1)])
        {
            QuoteDate = T(-1), QuoteStatus = QuoteStatuses.Draft,
            FollowUps = [new(T(-2), 11, "kevin", FollowUpTypes.Call, "林副理詢問工地臨時網路", "工期約 18 個月，需要監控與門禁網路。", 0, T(2), "完成報價送審")],
        };
        yield return new("新竹分所開設", "宏信會計", "david", T(-5), ProjectStatuses.Draft, 350000, [])
        {
            FollowUps = [new(T(-4), 15, "david", FollowUpTypes.Visit, "蘇會計師說明新分所需求", "約 25 人座位、需要獨立 VPN 連回總所。", 0, T(1), "提出規劃建議")],
        };
        yield return new("停車場監控", "海悅酒店", "kevin", M(4, 6), ProjectStatuses.Lost, 250000, [L("CM-IP4K", 16), L("CM-NVR32", 1), L("CB-CAT6", 16), L("SV-INST", 1)])
        {
            QuoteDate = M(3, 20), QuoteStatus = QuoteStatuses.Rejected, DecidedOn = M(2, 10), Reason = "客戶預算刪減，延到明年再議",
            FollowUps = [new(M(2, 10), 10, "kevin", FollowUpTypes.Call, "客戶告知預算刪減", "劉主任表示明年第一季會重新編列預算。", 1)],
        };
        yield return new("舊廠機房搬遷", "瑞峰精密", "amy", M(4, 1), ProjectStatuses.Cancelled, 260000, [L("SV-MOVE", 1), L("SV-R740", 1), L("SV-INST", 1)])
        { QuoteDate = M(4, 5), QuoteStatus = QuoteStatuses.Draft, EndedOn = M(3, 2), Reason = "客戶改由集團資訊部自行處理" };
    }

    private sealed record Deal(string Name, string Client, string Owner, DateOnly Created, string Status, decimal Expected, (string Code, decimal Quantity)[] Lines)
    {
        public string? Description { get; init; }
        public string? QuoteTitle { get; init; }
        public DateOnly? QuoteDate { get; init; }
        public DateOnly? RevisedFrom { get; init; }
        public string QuoteStatus { get; init; } = QuoteStatuses.Draft;
        public DateOnly? DecidedOn { get; init; }
        public decimal DiscountPercent { get; init; }
        public string? PaymentTerms { get; init; }
        public int OrderAfterDays { get; init; } = 7;
        public int DurationDays { get; init; } = 45;
        public string? OrderNote { get; init; }
        public Ship[] Shipments { get; init; } = [];
        public Bill[] Invoices { get; init; } = [];
        public int WarrantyMonths { get; init; }
        public int WarrantyStartAfterOrderDays { get; init; } = 30;
        public string? WarrantySubject { get; init; }
        public string? Reason { get; init; }
        public DateOnly? EndedOn { get; init; }
        public (string User, string Role)[] Members { get; init; } = [];
        public Visit[] FollowUps { get; init; } = [];
    }

    private sealed record Ship(int AfterOrderDays, string Status, int ContactIndex, string? Note);

    private sealed record Bill(decimal Ratio, int AfterOrderDays, int DueDays, bool Paid, string? Note = null);

    private sealed record Visit(DateOnly On, int Hour, string By, string Type, string Summary, string? Content = null, int ContactIndex = 0, DateOnly? Next = null, string? NextAction = null);
}
