using Commons.DTOs;
using NativeBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Topshelf;
using Topshelf.Configurators;

namespace RpcServer
{
    class Program
    {
        static void Main(string[] args)
        {
            int threadsCount = 5;

            HostFactory.Run(x =>
            {
                x.AddCommandLineDefinition("t", v =>
                {
                    int threadsLoc;
                    var parsed = int.TryParse(v, out threadsLoc);
                    if (parsed && threadsLoc > 0)
                    {
                        threadsCount = threadsLoc;
                    }
                });
                x.ApplyCommandLine();
                Console.WriteLine(threadsCount);
                var bus = new Bus(new LimitedConcurrencyLevelTaskScheduler(threadsCount));
                bus.Connect(new BusConnectDTO()
                    {
                        Hostname="rabbitmq.local",
                        Password = "betfred",
                        UserName = "betfred",
                        Port = 5672
                    });
                x.Service<Service>(sa =>
                {
                    sa.ConstructUsing(name => new Service(bus));
                    sa.WhenStarted(tc => tc.Start());
                    sa.WhenStopped(tc => tc.Stop());
                });

                x.RunAsLocalService();
            });
        }
    }
}
