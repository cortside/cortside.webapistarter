using System;
using System.Collections.Generic;
using Cortside.Common.BootStrap;
using Cortside.DomainEvent;
using Cortside.DomainEvent.EntityFramework;
using Cortside.DomainEvent.Events;
using Cortside.WebApiStarter.Data;
using Cortside.WebApiStarter.DomainEvent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cortside.WebApiStarter.BootStrap.Installer {
    public class DomainEventInstaller : IInstaller {
        public void Install(IServiceCollection services, IConfigurationRoot configuration) {
            var config = configuration.GetSection("ServiceBus");
            var rsettings = new ServiceBusReceiverSettings {
                Address = config.GetValue<string>("Queue"),
                AppName = config.GetValue<string>("AppName"),
                Protocol = config.GetValue<string>("Protocol"),
                PolicyName = config.GetValue<string>("Policy"),
                Key = config.GetValue<string>("Key"),
                Namespace = config.GetValue<string>("Namespace"),
                Durable = 1,
                Credits = config.GetValue<int>("Credits")
            };
            services.AddSingleton(rsettings);

            var psettings = new ServiceBusPublisherSettings {
                Address = config.GetValue<string>("Exchange"),
                AppName = config.GetValue<string>("AppName"),
                Protocol = config.GetValue<string>("Protocol"),
                PolicyName = config.GetValue<string>("Policy"),
                Key = config.GetValue<string>("Key"),
                Namespace = config.GetValue<string>("Namespace"),
                Durable = 1,
                Credits = config.GetValue<int>("Credits")
            };
            services.AddSingleton(psettings);

            // Register Hosted Services
            services.AddSingleton<IDomainEventPublisher, DomainEventPublisher>();
            services.AddTransient<IDomainEventOutboxPublisher, DomainEventOutboxPublisher<DatabaseContext>>();
            services.AddTransient<IDomainEventHandler<WidgetStageChangedEvent>, WidgetStateChangedHandler>();
            services.AddSingleton<IDomainEventReceiver, DomainEventReceiver>();

            // TODO: change settings to be Enabled instead of Disabled
            var receiverHostedServiceSettings = configuration.GetSection("ReceiverHostedService").Get<ReceiverHostedServiceSettings>();
            receiverHostedServiceSettings.MessageTypes = new Dictionary<string, Type> {
                { typeof(WidgetStageChangedEvent).FullName, typeof(WidgetStageChangedEvent) }
            };
            services.AddSingleton(receiverHostedServiceSettings);

            // outbox hosted service
            var outboxConfiguration = configuration.GetSection("OutboxHostedService").Get<OutboxHostedServiceConfiguration>();
            services.AddSingleton(outboxConfiguration);
            services.AddHostedService<OutboxHostedService<DatabaseContext>>();
        }
    }
}
