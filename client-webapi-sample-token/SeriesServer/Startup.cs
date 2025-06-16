// Macrobond Financial AB 2020-2025

using System;
using System.Text.Json.Serialization;
#if USEAUTHENTICATION
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

#endif
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SeriesServer
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IWebHostEnvironment environment)
        {
            Configuration = configuration;
            Environment = environment;
        }

        public const string AuthPolicyName = "BearerTokenPolicy";
        private IConfiguration Configuration { get; }
        private IWebHostEnvironment Environment { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc(options => options.EnableEndpointRouting = false);
            services.AddControllers().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });
            services.AddSwaggerGen();

#if USEAUTHENTICATION
            string authority = Configuration["Authentication:Authority"] ?? throw new InvalidOperationException("Configuration value for 'Authentication:Authority' is missing.");
            var audience = Configuration["Authentication:Audience"] ?? throw new InvalidOperationException("Configuration value for 'Authentication:Audience' is missing."); ;
            var role = Configuration["Authentication:Role"] ?? throw new InvalidOperationException("Configuration value for 'Authentication:Role' is missing."); ;

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = authority;

                    if (Environment.IsDevelopment())
                        options.RequireHttpsMetadata = false;
                    options.Audience = audience;
                    options.TokenValidationParameters.ClockSkew = TimeSpan.FromMinutes(2);    // Use 2 minutes instead of the default of 5 minutes
                });
            services.AddAuthorizationBuilder()
                .AddPolicy(AuthPolicyName, policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireRole(role);
                });
#endif
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI();
            }

#if USEAUTHENTICATION
            app.UseAuthentication();
#endif
            app.UseMvc();
        }
    }
}
