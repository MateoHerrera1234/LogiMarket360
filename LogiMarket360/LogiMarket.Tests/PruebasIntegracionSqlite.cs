using System;
using System.IO;
using System.Threading.Tasks;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Enums;
using LogiMarket.Infrastructure.Persistencia;
using Xunit;

namespace LogiMarket.Tests;

public sealed class PruebasIntegracionSqlite : IAsyncLifetime
{
    private readonly string _rutaDb = Path.Combine(Path.GetTempPath(), $"logimarket_test_{Guid.NewGuid():N}.db");
    private ContextoSqliteLogiMarket _contexto = null!;

    public async Task InitializeAsync()
    {
        _contexto = new ContextoSqliteLogiMarket(_rutaDb);
        await _contexto.InicializarEsquemaAsync();
    }

    public Task DisposeAsync()
    {
        _contexto.Dispose();
        if (File.Exists(_rutaDb))
        {
            try { File.Delete(_rutaDb); } catch { /* ignorar archivo bloqueado */ }
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RepositoriosSqlite_DebenPersistirYRecuperarProductoYCliente()
    {
        var repoCatalogo = new RepositorioCatalogoSqlite(_contexto);
        var repoClientes = new RepositorioClientesSqlite(_contexto);

        var prod = new ProductoSimple("SKU-INT-1", "Caja Integracion", 35.00m, 1.5m, 15);
        await repoCatalogo.GuardarProductoAsync(prod);

        var cli = new Cliente(Guid.NewGuid(), "CLI-INT-1", "Cliente Integracion", "int@test.ec", CategoriaCliente.MAYORISTA, true);
        await repoClientes.GuardarAsync(cli);

        var prodLeido = await repoCatalogo.ObtenerPorSkuAsync("SKU-INT-1");
        var cliLeido = await repoClientes.ObtenerPorIdAsync(cli.Id);

        Assert.NotNull(prodLeido);
        Assert.Equal(15, prodLeido!.StockDisponible);
        Assert.NotNull(cliLeido);
        Assert.Equal("CLI-INT-1", cliLeido!.Codigo);
    }
}
