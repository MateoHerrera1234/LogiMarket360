using System.Drawing;
using System.Windows.Forms;

namespace LogiMarket.WinForms;

internal sealed class LoginDialog : Form
{
    private readonly TextBox _usuario = new() { Text = "admin", Width = 220 };
    private readonly TextBox _contrasena = new() { Width = 220, UseSystemPasswordChar = true };

    public string NombreUsuario => _usuario.Text.Trim();
    public string Contrasena => _contrasena.Text;

    public LoginDialog()
    {
        Text = "Acceso a LogiMarket 360";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(340, 175);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "Usuario", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_usuario, 1, 0);
        layout.Controls.Add(new Label { Text = "Contraseña", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(_contrasena, 1, 1);

        var ingresar = new Button { Text = "Ingresar", DialogResult = DialogResult.OK, AutoSize = true };
        var cancelar = new Button { Text = "Salir", DialogResult = DialogResult.Cancel, AutoSize = true };
        var botones = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        botones.Controls.Add(cancelar);
        botones.Controls.Add(ingresar);
        layout.Controls.Add(botones, 0, 3);
        layout.SetColumnSpan(botones, 2);
        Controls.Add(layout);
        AcceptButton = ingresar;
        CancelButton = cancelar;
    }
}
