using System.Diagnostics;
using Firebase.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QASystem.Hubs;
using QASystem.Models;
using QASystem.Services;

var builder = WebApplication.CreateBuilder(args);

// Thêm dịch vụ MVC
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

//config firebase
var firebaseConfig = builder.Configuration.GetSection("Firebase");
var projectId = firebaseConfig["ProjectId"];
var jsonCredentialsPath = firebaseConfig["JsonCredentialsPath"];
// Kiểm tra file JSON có tồn tại không
if (!File.Exists(jsonCredentialsPath))
{
    Console.WriteLine($"ERROR: Firebase credentials file not found at: {jsonCredentialsPath}");
    throw new FileNotFoundException($"Firebase credentials file not found at: {jsonCredentialsPath}");
}
else
{
    Console.WriteLine($"Firebase credentials file found at: {jsonCredentialsPath}");
}
// Đăng ký FirebaseStorage
builder.Services.AddSingleton<FirebaseStorage>(sp =>
    new FirebaseStorage(projectId, new FirebaseStorageOptions
    {
        AuthTokenAsyncFactory = () => Task.FromResult(File.ReadAllText(jsonCredentialsPath))
    }));

// Thêm DbContext
builder.Services.AddDbContext<QasystemContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("QASystem")));

builder.Services.AddIdentity<User, IdentityRole<int>>()
    .AddEntityFrameworkStores<QasystemContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Home/AccessDenied";
});

// Thêm session
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Thêm dịch vụ email
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddScoped<IEmailService, EmailService>();

var app = builder.Build();

// Cấu hình middleware
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// Xử lý lỗi 404 và 403 tùy chỉnh
app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    if (response.StatusCode == 403)
    {
        response.Redirect("/Home/AccessDenied");
    }
    else if (response.StatusCode == 404)
    {
        response.Redirect("/Home/NotFound");
    }
});

// Route mặc định
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapHub<QuestionHub>("/questionHub"); // Định nghĩa route cho Hub

app.Run();