using System;
using System.Windows.Forms;
using LogiMarket.WinForms.Coordinacion;

namespace LogiMarket.WinForms;

public partial class Form1 : Form
{
    private readonly CoordinadorLogiMarketWinForms _coordinador = new();

    public Form1()
    {
        InitializeComponent();
        Load += Form1_Load;
        FormClosed += (_, _) => _coordinador.Dispose();
    }

    private async void Form1_Load(object? sender, EventArgs e)
    {
        try
        {
            await _coordinador.InicializarAsync();
            Text = $"LogiMarket 360 - Rol: {_coordinador.ContextoSqlite.UsuarioActual?.Rol}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Error al inicializar LogiMarket 360", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
