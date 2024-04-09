
using Appota.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Appota.Schedule;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSession(); // Thêm dịch vụ Session vào container


var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"),
    sqlServerOptionsAction: sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(); 
    }

    ));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.UseSession(); // Kích hoạt việc sử dụng Session


app.UseEndpoints(endpoints =>
{
    endpoints.MapControllerRoute(
      name: "MyArea",
      pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}"
    );
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run(async context =>
{
    var session = context.RequestServices.GetRequiredService<ISession>();
    session.SetString("key", "value"); // Đặt giá trị vào Session

    // Đọc giá trị từ Session
    var value = session.GetString("key");

    await context.Response.WriteAsync($"Session value: {value}");
});

// Lập lịch execute stored procedure
var serviceProvider = builder.Services.BuildServiceProvider();
var dbContext = serviceProvider.GetRequiredService<ApplicationDbContext>();
var scheduler = new Scheduler(dbContext);
scheduler.Start();

app.Run();
