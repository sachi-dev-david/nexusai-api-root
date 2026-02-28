using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using NexusAI.Api.Data;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.SemanticKernel;
using NexusAI.Api.Plugins;
using NexusAI.Api.Services;
using NexusAI.Api.Services.Impl;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ── CONTROLLERS ───────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase);

// ── JWT 認證 ──────────────────────────────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]
             ?? throw new InvalidOperationException("appsettings.json 缺少 Jwt:Key");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization();

// ── CORS ──────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    // 開發用：允許任何來源
    options.AddPolicy("DevPolicy", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });

    // 正式環境用：只允許前端網域
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins("https://nexusai.sachitw.com")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});


// ── SWAGGER ───────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "NexusAI API",
        Version = "v1",
        Description = "工廠智能助手後端 API"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "輸入 JWT Token（不需加 Bearer 前綴）"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                    { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
    var xmlPath = Path.Combine(AppContext.BaseDirectory,
        $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlPath)) c.IncludeXmlComments(xmlPath);
});

// ── MARIADB + EF CORE ────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("MariaDB")
        ?? throw new InvalidOperationException("ConnectionStrings:MariaDB 未設定"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("MariaDB")!),
        mysql => mysql.EnableRetryOnFailure(3)
    )
);

// ── MLDATABASE（報價系統）────────────────────────────────────────────────
builder.Services.AddDbContextFactory<MlDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("MlDatabase")
        ?? throw new InvalidOperationException("ConnectionStrings:MlDatabase 未設定"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("MlDatabase")!),
        mysql => mysql.EnableRetryOnFailure(3)
    )
);

// ── SEMANTIC KERNEL + OLLAMA ──────────────────────────────────────────────
#pragma warning disable SKEXP0001, SKEXP0010, SKEXP0070
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var ollamaUrl = cfg["Ollama:BaseUrl"] ?? "http://localhost:11434";
    var model = cfg["Ollama:Model"] ?? "mistral";
    var logFactory = sp.GetRequiredService<ILoggerFactory>();
    var logger = logFactory.CreateLogger("OllamaSetup");

    logger.LogInformation("初始化 Semantic Kernel，Ollama={Url}，Model={Model}", ollamaUrl, model);

    // Ollama 相容 OpenAI 格式，使用 v1/chat/completions 端點
    // 需指定完整的 /v1 路徑，否則 SK 預設會接錯端點
    var ollamaEndpoint = new Uri(ollamaUrl.TrimEnd('/') + "/v1");

    var kernel = Kernel.CreateBuilder()
        .AddOpenAIChatCompletion(
            modelId: model,
            endpoint: ollamaEndpoint,
            apiKey: "ollama") // Ollama 不驗證 Key，任意字串皆可
        .Build();

    // 註冊所有 Skill Plugins
    var mlDbFactory = sp.GetRequiredService<IDbContextFactory<MlDbContext>>();

    kernel.Plugins.AddFromObject(
        new MachineStatusPlugin(logFactory.CreateLogger<MachineStatusPlugin>()), "MachineStatus");
    kernel.Plugins.AddFromObject(
        new QuotePlugin(mlDbFactory, logFactory.CreateLogger<QuotePlugin>()), "Quote");
    kernel.Plugins.AddFromObject(
        new ProcessPlugin(logFactory.CreateLogger<ProcessPlugin>()), "Process");
    kernel.Plugins.AddFromObject(
        new RagPlugin(logFactory.CreateLogger<RagPlugin>()), "Rag");

    logger.LogInformation("Semantic Kernel 初始化完成，已載入 {Count} 個 Plugin", kernel.Plugins.Count());

    return kernel;
});
#pragma warning restore SKEXP0001, SKEXP0010, SKEXP0070

// ── APPLICATION SERVICES ──────────────────────────────────────────────────
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<FileService>();

// ── BUILD ─────────────────────────────────────────────────────────────────
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "NexusAI API v1");
        c.RoutePrefix = "swagger";
    });
}

// 如果是開發環境
if (app.Environment.IsDevelopment())
{
    app.UseCors("DevPolicy");
}
else
{
    app.UseCors("FrontendPolicy");
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ── 診斷端點：GET /api/health/ollama（不需要登入）──────────────────────
app.MapGet("/api/health/ollama", async (IConfiguration cfg) =>
{
    var ollamaUrl = cfg["Ollama:BaseUrl"] ?? "http://localhost:11434";
    var model = cfg["Ollama:Model"] ?? "mistral";
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var res = await http.GetAsync($"{ollamaUrl}/api/tags");
        var body = await res.Content.ReadAsStringAsync();
        return Results.Ok(new
            { status = "ok", ollamaUrl, configModel = model, httpStatus = (int)res.StatusCode, body });
    }
    catch (Exception ex)
    {
        return Results.Ok(new
        {
            status = "error", ollamaUrl, configModel = model, error = ex.Message, tip = "請確認 Ollama 已執行：ollama serve"
        });
    }
});

app.Run();