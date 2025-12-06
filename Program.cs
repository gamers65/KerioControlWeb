using KerioControlWeb.Services;

var builder = WebApplication.CreateBuilder(args);

// Добавление сервисов
builder.Services.AddControllersWithViews();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Регистрация сервисов
builder.Services.AddSingleton<IKerioApiService, KerioApiService>();
builder.Services.AddSingleton<ILogService, FileLogService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IFileProcessingService, FileProcessingService>();

builder.Services.AddHttpClient("PythonIocService", client =>
{
    client.BaseAddress = new Uri("http://localhost:8000");
});

builder.WebHost.UseUrls("http://0.0.0.0:5043", "https://0.0.0.0:7135");
builder.Services.AddHttpClient();

builder.Services.AddSingleton(provider =>
{
    var env = provider.GetRequiredService<IWebHostEnvironment>();
    var path = Path.Combine(env.ContentRootPath, "Data", "exclude.txt");
    return new ExcludeService(path);
});

var app = builder.Build();

// Конфигурация pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();