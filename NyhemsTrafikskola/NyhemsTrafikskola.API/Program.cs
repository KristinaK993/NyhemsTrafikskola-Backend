using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using NyhemsTrafikskola.Infrastructure;
using NyhemsTrafikskola.Infrastructure.Auth;

namespace NyhemsTrafikskola.API
{
    public partial class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddInfrastructure(builder.Configuration);

            builder.Services.AddCors(options =>
            {
                var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ??
                    ["https://localhost:3000", "https://localhost:5173"];

                options.AddPolicy("FrontendPolicy", policy =>
                {
                    policy.WithOrigins(allowedOrigins)
                        .AllowCredentials()
                        .WithExposedHeaders("X-XSRF-TOKEN")
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
            });

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            builder.Services.AddAntiforgery(options =>
            {
                options.HeaderName = "X-XSRF-TOKEN";
                options.Cookie.Name = "NyhemsTrafikskola.XSRF";
                options.Cookie.HttpOnly = false;
                options.Cookie.IsEssential = true;
                options.Cookie.SameSite = SameSiteMode.None;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            });

            var app = builder.Build();

            using (var scope = app.Services.CreateScope())
            {
                var roleInitializer = scope.ServiceProvider.GetRequiredService<RoleInitializer>();
                roleInitializer.EnsureRolesAsync().GetAwaiter().GetResult();
            }

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseCors("FrontendPolicy");
            app.UseAuthentication();
            app.Use(async (context, next) =>
            {
                if ((HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method) || HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method))
                    && context.User.Identity?.IsAuthenticated == true)
                {
                    var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();

                    try
                    {
                        await antiforgery.ValidateRequestAsync(context);
                    }
                    catch (AntiforgeryValidationException)
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsJsonAsync(new { message = "Invalid CSRF token." });
                        return;
                    }
                }

                await next();
            });
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }

    public partial class Program { }
}
