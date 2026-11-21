using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LogiMarket.Application.Puertos;

namespace LogiMarket.Infrastructure.Documentos;

// Salida concreta del patron Bridge que guarda comprobantes en archivos de texto locales
public sealed class SalidaArchivoComprobante : ISalidaDocumento
{
    private readonly string _carpetaSalida;

    public string NombreMedio => "ARCHIVO_TXT";

    public SalidaArchivoComprobante(string? carpetaSalida = null)
    {
        if (string.IsNullOrWhiteSpace(carpetaSalida))
        {
            _carpetaSalida = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LogiMarket360",
                "Comprobantes");
        }
        else
        {
            _carpetaSalida = carpetaSalida;
        }

        Directory.CreateDirectory(_carpetaSalida);
    }

    public async Task<string> EmitirAsync(string titulo, string contenido, CancellationToken ct = default)
    {
        string nombreSeguro = string.Join("_", titulo.Split(Path.GetInvalidFileNameChars()));
        string archivo = Path.Combine(_carpetaSalida, $"{nombreSeguro}_{DateTime.UtcNow:yyyyMMddHHmmssfff}.txt");

        var sb = new StringBuilder();
        sb.AppendLine($"=== {titulo} ===");
        sb.AppendLine($"FECHA UTC: {DateTime.UtcNow:O}");
        sb.AppendLine();
        sb.Append(contenido);

        await File.WriteAllTextAsync(archivo, sb.ToString(), ct);
        return archivo;
    }
}

// Salida concreta del patron Bridge que devuelve el texto formateado en memoria
public sealed class SalidaTextoMemoria : ISalidaDocumento
{
    public string NombreMedio => "TEXTO_MEMORIA";

    public Task<string> EmitirAsync(string titulo, string contenido, CancellationToken ct = default)
    {
        string texto = $"[{titulo}] -> {contenido.Replace(Environment.NewLine, " | ")}";
        return Task.FromResult(texto);
    }
}