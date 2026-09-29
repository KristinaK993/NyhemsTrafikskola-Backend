using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using System.Text;
using NyhemsTrafikskola.Infrastructure;
using NyhemsTrafikskola.Infrastructure.Auth;

namespace NyhemsTrafikskola.API
{
    public partial class Program
    {
        private const string FirstAdminCommand = "--create-first-admin";

        public static async Task Main(string[] args)
        {
            var createFirstAdmin = args.Contains(FirstAdminCommand, StringComparer.Ordinal);
            var builderArgs = args.Where(argument => !string.Equals(argument, FirstAdminCommand, StringComparison.Ordinal)).ToArray();
            var builder = WebApplication.CreateBuilder(builderArgs);

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

            if (createFirstAdmin)
            {
                await RunFirstAdminSetupAsync(app);
                await app.DisposeAsync();
                return;
            }

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

        private static async Task RunFirstAdminSetupAsync(WebApplication app)
        {
            if (Console.IsInputRedirected)
            {
                Console.Error.WriteLine("First-admin setup requires an interactive local terminal.");
                Environment.ExitCode = 2;
                return;
            }

            Console.Write("Administrator email: ");
            var email = Console.ReadLine();
            Console.Write("First name: ");
            var firstName = Console.ReadLine();
            Console.Write("Last name: ");
            var lastName = Console.ReadLine();

            var password = ReadPassword("Password: ");
            var passwordConfirmation = ReadPassword("Confirm password: ");
            Console.WriteLine();

            if (!string.Equals(password, passwordConfirmation, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Passwords do not match. No account was created.");
                Environment.ExitCode = 2;
                return;
            }

            using var scope = app.Services.CreateScope();
            var provisioner = scope.ServiceProvider.GetRequiredService<FirstAdminProvisioner>();
            var result = await provisioner.CreateFirstAdminAsync(email ?? string.Empty, password, firstName ?? string.Empty, lastName ?? string.Empty);

            if (result.WasCreated)
            {
                Console.WriteLine("The first administrator was created successfully.");
                return;
            }

            if (result.AdminAlreadyExists)
            {
                Console.WriteLine("An administrator already exists. No account was created.");
                return;
            }

            foreach (var error in result.Errors)
            {
                Console.Error.WriteLine(error);
            }

            Environment.ExitCode = 1;
        }

        private static string ReadPassword(string prompt)
        {
            Console.Write(prompt);
            var password = new StringBuilder();

            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    break;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (password.Length > 0)
                    {
                        password.Length--;
                        Console.Write("\b \b");
                    }

                    continue;
                }

                if (!char.IsControl(key.KeyChar))
                {
                    password.Append(key.KeyChar);
                    Console.Write('*');
                }
            }

            Console.WriteLine();
            return password.ToString();
        }
    }

    public partial class Program { }
}
