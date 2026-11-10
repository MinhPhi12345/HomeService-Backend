using System.Text;
using HomeService.Api.Common;
using HomeService.Api.Data;
using HomeService.Api.Middleware;
using HomeService.Api.Options;
using HomeService.Api.Services;
using HomeService.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// Log tiếng Việt có dấu không bị lỗi font khi ghi ra file stdout (IIS / Plesk)
Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// Bí mật của bản deploy (chuỗi kết nối, JWT key, mật khẩu Admin) – file chỉ nằm trên máy chủ, KHÔNG commit (.gitignore).
// Mẫu: appsettings.Secrets.example.json. Biến môi trường vẫn được ưu tiên hơn file này.
builder.Configuration.AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();

// Thư mục lưu ảnh tải lên (POST /api/uploads) – cấu hình Storage:UploadsPath, tạo trước để phục vụ /uploads/...
var uploadsPath = StorageOptions.ResolveUploadsPath(builder.Configuration, builder.Environment.ContentRootPath);
Directory.CreateDirectory(uploadsPath);
builder.Services.Configure<StorageOptions>(o => o.UploadsPath = uploadsPath);

// Chạy sau reverse proxy HTTPS (Azure App Service, Nginx…): nhận đúng scheme/IP gốc của người dùng
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// --- Database (SQL Server) ---
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// --- Dependency Injection ---
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<MatchingService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<SettlementService>();
builder.Services.AddScoped<OfferService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<BookingJobs>();
builder.Services.AddScoped<CancellationService>();
builder.Services.AddScoped<TaskerDisciplineService>();
builder.Services.AddScoped<DisputeService>();
// Mã OTP qua email (Gmail SMTP – cấu hình "Email" trong appsettings.Secrets.json)
builder.Services.AddScoped<OtpService>();
builder.Services.AddScoped<VoucherService>();
builder.Services.AddScoped<PromotionService>();
builder.Services.AddScoped<TaskerAwardService>();
builder.Services.AddScoped<AccountEmailService>();
builder.Services.AddScoped<AccountLockService>();
builder.Services.AddScoped<AppealService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
// Bản đồ & tra địa chỉ OpenStreetMap đi qua Backend (cache + User-Agent riêng)
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient(GeoService.HttpClientName, (sp, client) =>
{
    var map = builder.Configuration.GetSection(MapProxyOptions.Section).Get<MapProxyOptions>() ?? new MapProxyOptions();
    client.DefaultRequestHeaders.UserAgent.ParseAdd(map.UserAgent);
    client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddSingleton<GeoService>();

// --- Cấu hình nghiệp vụ (appsettings.json) ---
builder.Services.Configure<BusinessOptions>(builder.Configuration.GetSection(BusinessOptions.Section));
builder.Services.Configure<MatchingOptions>(builder.Configuration.GetSection(MatchingOptions.Section));
builder.Services.Configure<SettlementOptions>(builder.Configuration.GetSection(SettlementOptions.Section));
builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.Section));
builder.Services.Configure<MapProxyOptions>(builder.Configuration.GetSection(MapProxyOptions.Section));
builder.Services.Configure<OtpOptions>(builder.Configuration.GetSection(OtpOptions.Section));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.Section));
builder.Services.Configure<AwardOptions>(builder.Configuration.GetSection(AwardOptions.Section));
builder.Services.Configure<PromotionOptions>(builder.Configuration.GetSection(PromotionOptions.Section));
builder.Services.Configure<AccountOptions>(builder.Configuration.GetSection(AccountOptions.Section));

// --- Tác vụ nền: ghép nối theo đợt, hết hạn thanh toán, tự xác nhận... ---
builder.Services.AddHostedService<BookingWorker>();

// --- Authentication (JWT) ---
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        options.Events = new JwtBearerEvents
        {
            // US06 / BR-A03: tài khoản bị khóa (hoặc đã xóa) thì mọi token cũ bị từ chối ở lần gọi kế tiếp → 401
            OnTokenValidated = async context =>
            {
                var sub = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? context.Principal?.FindFirst("sub")?.Value;
                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var active = int.TryParse(sub, out var userId)
                             && await db.Users.AnyAsync(u => u.Id == userId && u.IsActive);
                if (!active) context.Fail("Tài khoản không còn hoạt động.");
            },
        };
    });

builder.Services.AddAuthorization();

// --- CORS (cho web-customer, web-admin, mobile gọi API) ---
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddRouting(o => o.LowercaseUrls = true); // SRS 8.1: đường dẫn viết chữ thường (/api/bookings)
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Encoder = JsonDefaults.Encoder); // giữ tiếng Việt có dấu
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "HomeService API", Version = "v1" });
    var jwtScheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Scheme = "bearer",
        BearerFormat = "JWT",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
    };
    c.AddSecurityDefinition("Bearer", jwtScheme);
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        { new Microsoft.OpenApi.Models.OpenApiSecurityScheme { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});

var app = builder.Build();

// Tự áp dụng migration + tạo tài khoản Admin mặc định
await DbInitializer.InitializeAsync(app.Services);

app.UseForwardedHeaders();
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Swagger: luôn bật khi Development; bản deploy bật bằng Swagger__Enabled=true (để demo / chấm đồ án)
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions // ảnh đã tải lên: /uploads/...
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public,max-age=604800",
});
app.UseCors("Default");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
// Kiểm tra Backend còn sống – nhận cả GET và HEAD (UptimeRobot mặc định gửi HEAD)
app.MapMethods("/healthz", new[] { "GET", "HEAD" }, () => Results.Ok(new { status = "ok" }));

app.Run();
