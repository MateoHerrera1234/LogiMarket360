using Microsoft.Extensions.DependencyInjection;
using LogiMarket.Application.Creacionales;

namespace LogiMarket.WinForms
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddSingleton<IMetricasSesion>(MetricasSesion.Instancia);
            services.AddSingleton<Coordinacion.CoordinadorLogiMarketWinForms>();
            services.AddTransient<Form1>();
            using var provider = services.BuildServiceProvider();
            System.Windows.Forms.Application.Run(provider.GetRequiredService<Form1>());
        }
    }
}
