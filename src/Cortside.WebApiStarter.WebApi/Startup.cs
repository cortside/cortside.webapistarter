using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Reflection;
using AutoMapper;
using Cortside.Common.BootStrap;
using Cortside.Common.Correlation;
using Cortside.Common.DomainEvent;
using Cortside.Common.Json;
using Cortside.DomainEvent.Events;
using Cortside.Health;
using Cortside.Health.Checks;
using Cortside.Health.Controllers;
using Cortside.Health.Models;
using Cortside.Health.Recorders;
using Cortside.WebApiStarter.BootStrap;
using Cortside.WebApiStarter.Data;
using Cortside.WebApiStarter.DomainService;
using Cortside.WebApiStarter.WebApi.Events;
using IdentityServer4.AccessTokenValidation;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Serilog;


namespace Cortside.WebApiStarter.WebApi {
    /// <summary>
    /// Startup
    /// </summary>
    public class Startup {
        private readonly BootStrapper bootstrapper = null;

        /// <summary>
        /// Startup
        /// </summary>
        /// <param name="configuration"></param>
        public Startup(IConfiguration configuration) {
            bootstrapper = new DefaultApplicationBootStrapper();
            Configuration = configuration;
        }

        /// <summary>
        /// Config
        /// </summary>
        public IConfiguration Configuration { get; }

        /// <summary>
        /// Configure Services
        /// </summary>
        /// <param name="services"></param>
        public void ConfigureServices(IServiceCollection services) {
            services.AddDbContext<DatabaseContext>(opt => {
                opt.UseSqlServer(Configuration.GetSection("WebApiStarter").GetValue<string>("ConnectionString"));
                opt.EnableSensitiveDataLogging();
            });
            // TODO: this is creating another instance that is different than the one above and does not have EnableSensitiveDataLogging set on it -- need to fix
            services.AddTransient<IDatabaseContext, DatabaseContext>();

            services.AddResponseCaching();
            services.AddResponseCompression(options => {
                options.Providers.Add<GzipCompressionProvider>();
            });

            services.AddMemoryCache();
            services.AddCors();

            IsoDateTimeConverter isoConverter = new IsoDateTimeConverter {
                DateTimeFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'"
            };

            IsoTimeSpanConverter isoTimeSpanConverter = new IsoTimeSpanConverter();

            JsonConvert.DefaultSettings = () => {
                var settings = new JsonSerializerSettings();
                settings.Converters.Add(new StringEnumConverter(new CamelCaseNamingStrategy()));
                settings.Converters.Add(isoConverter);
                settings.Converters.Add(isoTimeSpanConverter);
                return settings;
            };

            services.AddControllers(options => {
                options.OutputFormatters.Add(new XmlDataContractSerializerOutputFormatter());
                options.CacheProfiles.Add("default", new CacheProfile {
                    Duration = 30,
                    Location = ResponseCacheLocation.Any
                });
                //options.Filters.Add<MessageExceptionResponseFilter>();
            })
                .AddNewtonsoftJson(options => {
                    options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                    options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                    options.SerializerSettings.DateFormatHandling = DateFormatHandling.IsoDateFormat;
                    options.SerializerSettings.Converters.Add(new StringEnumConverter(new CamelCaseNamingStrategy()));
                    options.SerializerSettings.Converters.Add(isoConverter);
                    options.SerializerSettings.Converters.Add(isoTimeSpanConverter);
                })
                .PartManager.ApplicationParts.Add(new AssemblyPart(typeof(HealthController).Assembly));


            services.AddRouting(options => {
                options.LowercaseUrls = true;
            });

            services.AddSingleton<IActionContextAccessor, ActionContextAccessor>();
            services.AddScoped(sp => {
                return sp.GetRequiredService<IUrlHelperFactory>().GetUrlHelper(sp.GetRequiredService<IActionContextAccessor>().ActionContext);
            });

            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

            var authConfig = Configuration.GetSection("CortsideIdentityApi");
            services.AddAuthentication(IdentityServerAuthenticationDefaults.AuthenticationScheme)
                .AddIdentityServerAuthentication(options => {
                    // base-address of your identityserver
                    options.Authority = authConfig.GetValue<string>("Authority");
                    options.RequireHttpsMetadata = authConfig.GetValue<bool>("RequireHttpsMetadata");
                    options.RoleClaimType = "role";
                    options.NameClaimType = "name";

                    // name of the API resource
                    options.ApiName = authConfig.GetValue<string>("ApiName");
                    options.ApiSecret = authConfig.GetValue<string>("ApiSecret");

                    options.EnableCaching = true;
                    options.CacheDuration = TimeSpan.FromMinutes(10);
                });

            services.AddOptions();
            services.AddHttpContextAccessor();

            // register all services, handlers and factories
            Assembly.GetEntryAssembly().GetTypes()
                .Where(x => (x.Name.EndsWith("Service") || x.Name.EndsWith("Handler") || x.Name.EndsWith("Factory") || x.Name.EndsWith("Client"))
                    && x.GetTypeInfo().IsClass
                    && !x.GetTypeInfo().IsAbstract
                    && x.GetInterfaces().Any())
                .ToList().ForEach(x => {
                    x.GetInterfaces().ToList()
                        .ForEach(i => services.AddSingleton(i, x));
                });

            // register domain services
            typeof(SubjectService).GetTypeInfo().Assembly.GetTypes()
                .Where(x => (x.Name.EndsWith("Service"))
                    && x.GetTypeInfo().IsClass
                    && !x.GetTypeInfo().IsAbstract
                    && x.GetInterfaces().Any())
                .ToList().ForEach(x => {
                    x.GetInterfaces().ToList()
                        .ForEach(i => services.AddSingleton(i, x));
                });

            services.AddSwaggerGen(c => {
                c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Version = "v1", Title = "WebApiStarter API" });
                //c.DescribeAllEnumsAsStrings();

                // Set the comments path for the Swagger JSON and UI.
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);
            });

            // Register Hosted Services
            services.AddSingleton<IDomainEventPublisher, DomainEventPublisher>();
            services.AddTransient<IDomainEventHandler<WebApiStarterCreationEvent>, WebApiStarterCreationEventHandler>();
            services.AddSingleton<IDomainEventReceiver, DomainEventReceiver>();

            services.AddSingleton<ITelemetryInitializer, AppInsightsInitializer>();
            services.AddApplicationInsightsTelemetry();

            services.AddAutoMapper(typeof(Startup).Assembly);
            services.AddPolicyServerRuntimeClient(Configuration.GetSection("PolicyServer"))
                .AddAuthorizationPermissionPolicies();
            services.AddSingleton<ISubjectService, SubjectService>();

            var receiverHostedServiceSettings = Configuration.GetSection("ReceiverHostedService").Get<ReceiverHostedServiceSettings>();
            receiverHostedServiceSettings.MessageTypes = new Dictionary<string, Type> {
                { typeof(WebApiStarterCreationEvent).FullName, typeof(WebApiStarterCreationEvent) }
            };
            services.AddSingleton(receiverHostedServiceSettings);

            // health checks
            // telemetry recorder
            string instrumentationKey = Configuration["ApplicationInsights:InstrumentationKey"];
            string endpointAddress = Configuration["ApplicationInsights:EndpointAddress"];
            if (!string.IsNullOrEmpty(instrumentationKey) && !string.IsNullOrEmpty(endpointAddress)) {
                TelemetryConfiguration telemetryConfiguration = new TelemetryConfiguration(instrumentationKey, new InMemoryChannel { EndpointAddress = endpointAddress });
                TelemetryClient telemetryClient = new TelemetryClient(telemetryConfiguration);
                services.AddSingleton(telemetryClient);
                services.AddTransient<IAvailabilityRecorder, ApplicationInsightsRecorder>();
            } else {
                services.AddTransient<IAvailabilityRecorder, NullRecorder>();
            }

            // configuration
            services.AddSingleton(Configuration.GetSection("HealthCheckHostedService").Get<HealthCheckServiceConfiguration>());
            services.AddSingleton(Configuration.GetSection("Build").Get<BuildModel>());

            // checks
            services.AddTransient<UrlCheck>();
            services.AddTransient<DbContextCheck>();

            // check factory and hosted service
            services.AddTransient<ICheckFactory, CheckFactory>();
            services.AddHostedService<HealthCheckHostedService>();

            // for DbContextCheck
            services.AddTransient<DbContext, DatabaseContext>();

            services.AddSingleton(Configuration);
            bootstrapper.InitIoCContainer(Configuration as IConfigurationRoot, services);
        }

        /// <summary>
        /// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        /// </summary>
        /// <param name="app"></param>
        /// <param name="env"></param>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env) {
            app.UseMiddleware<CorrelationMiddleware>();

            app.UseSwagger();
            app.UseSwaggerUI(c => {
                var s = "/swagger/v1/swagger.json";
                c.SwaggerEndpoint(s, "WebApiStarter Api V1");
            });

            if (env.IsDevelopment()) {
                app.UseDeveloperExceptionPage();
            }

            app.UseSerilogRequestLogging();

            app.UseCors(builder => builder
                .WithOrigins(Configuration.GetSection("Cors").GetSection("Origins").Get<string[]>())
                .SetIsOriginAllowedToAllowWildcardSubdomains()
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials());

            app.UseAuthentication();
            app.UseRouting();
            app.UseEndpoints(
                endpoints => {
                    endpoints.MapControllers();
                });
        }
    }
}
