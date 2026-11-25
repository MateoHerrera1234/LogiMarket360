using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogiMarket.Application.Comportamentales;
using LogiMarket.Application.Creacionales;
using LogiMarket.Application.Estructurales;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Enums;
using LogiMarket.Domain.Pedidos;
using LogiMarket.WinForms.Coordinacion;
using LogiMarket.Infrastructure.Documentos;

namespace LogiMarket.WinForms;

public partial class Form1 : Form
{
    private readonly CoordinadorLogiMarketWinForms _coord;
    private FormularioPrincipalMediator _mediador = null!;

    private TextBox _txtUsuario = null!;
    private TextBox _txtContrasena = null!;
    private TabControl _tabs = null!;
    private Label _lblMetricas = null!;

    // Catalogo y Kits
    private DataGridView _dgvCatalogo = null!;
    private TextBox _txtSku = null!;
    private TextBox _txtNombreProd = null!;
    private NumericUpDown _numPrecio = null!;
    private NumericUpDown _numPeso = null!;
    private NumericUpDown _numStock = null!;
    private TextBox _txtKitSku = null!;
    private TextBox _txtKitNombre = null!;
    private NumericUpDown _numKitPrecio = null!;
    private ComboBox _cmbCompKit = null!;
    private NumericUpDown _numCompCant = null!;
    private TextBox _txtResumenVisitor = null!;

    // Clientes
    private DataGridView _dgvClientes = null!;
    private TextBox _txtCliCodigo = null!;
    private TextBox _txtCliNombre = null!;
    private TextBox _txtCliContacto = null!;
    private ComboBox _cmbCliCategoria = null!;

    // Pedidos y Borrador
    private ComboBox _cmbPedidoCliente = null!;
    private ComboBox _cmbPedidoEntrega = null!;
    private ComboBox _cmbPedidoElemento = null!;
    private NumericUpDown _numPedidoCant = null!;
    private DataGridView _dgvBorrador = null!;
    private Label _lblTotalBorrador = null!;
    private TextBox _txtClaveIdempotencia = null!;
    private TextBox _txtFiltroPedidos = null!;
    private DataGridView _dgvPedidos = null!;

    // Despacho y Devoluciones
    private ComboBox _cmbPedidoOp = null!;
    private ComboBox _cmbMotivoDev = null!;
    private TextBox _txtObsDev = null!;
    private CheckBox _chkProductoSinUso = null!;
    private TextBox _txtBitacoraOp = null!;

    // Auditoria y Outbox
    private DataGridView _dgvAuditoria = null!;
    private DataGridView _dgvMovimientos = null!;
    private Label _lblOutboxPendientes = null!;

    public Form1(CoordinadorLogiMarketWinForms coord)
    {
        _coord = coord ?? throw new ArgumentNullException(nameof(coord));
        InitializeComponent();
        ConfigurarInterfaz();
        _mediador = new FormularioPrincipalMediator(
            RefrescarCatalogoAsync,
            RefrescarClientesAsync,
            RefrescarPedidosAsync,
            RefrescarAuditoriaAsync,
            RefrescarBorradorVisual,
            ActualizarBarraEstado);
        Load += Form1_Load;
    }

    private void ConfigurarInterfaz()
    {
        Text = "LogiMarket 360 - Sistema de Gestion Logistica y Comercial";
        Size = new Size(1240, 760);
        StartPosition = FormStartPosition.CenterScreen;

        var barraSuperior = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.FromArgb(240, 244, 248), Padding = new Padding(10, 8, 10, 8) };
        var lblAcceso = new Label { Text = "Acceso:", AutoSize = true, Location = new Point(12, 16), Font = new Font(Font, FontStyle.Bold) };
        _txtUsuario = new TextBox { Location = new Point(78, 11), Width = 125, Text = "admin", AccessibleName = "Usuario" };
        _txtContrasena = new TextBox { Location = new Point(210, 11), Width = 150, UseSystemPasswordChar = true, AccessibleName = "Contraseña" };
        var btnAcceso = new Button { Text = "Ingresar", Location = new Point(366, 9), Width = 85, Height = 28 };
        btnAcceso.Click += async (_, _) => await IniciarSesionDesdeCamposAsync();
        var btnCerrarSesion = new Button { Text = "Cerrar sesión", Location = new Point(456, 9), Width = 100, Height = 28 };
        btnCerrarSesion.Click += async (_, _) => await CerrarSesionAsync();

        _lblMetricas = new Label { AutoSize = true, Location = new Point(570, 16), Text = "Inicie sesión para continuar" };
        barraSuperior.Controls.AddRange(new Control[] { lblAcceso, _txtUsuario, _txtContrasena, btnAcceso, btnCerrarSesion, _lblMetricas });

        _tabs = new TabControl { Dock = DockStyle.Fill, Enabled = false };
        _tabs.TabPages.Add(CrearTabCatalogo());
        _tabs.TabPages.Add(CrearTabClientes());
        _tabs.TabPages.Add(CrearTabPedidos());
        _tabs.TabPages.Add(CrearTabDespachoDevoluciones());
        _tabs.TabPages.Add(CrearTabAuditoria());

        Controls.Add(_tabs);
        Controls.Add(barraSuperior);
    }

    private TabPage CrearTabCatalogo()
    {
        var page = new TabPage("1. Catalogo y Kits (Composite)");
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 700 };

        _dgvCatalogo = CrearGridSoloLectura();
        split.Panel1.Controls.Add(CrearContenedorGrid(_dgvCatalogo));

        var panelDer = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
        int y = 10;

        panelDer.Controls.Add(new Label { Text = "Nuevo Producto Simple", Font = new Font(Font, FontStyle.Bold), Location = new Point(10, y), AutoSize = true });
        y += 24;
        _txtSku = AgregarCampoTexto(panelDer, "SKU:", ref y, "SKU-201");
        _txtNombreProd = AgregarCampoTexto(panelDer, "Nombre:", ref y, "Producto Nuevo");
        _numPrecio = AgregarCampoDecimal(panelDer, "Precio USD:", ref y, 10m);
        _numPeso = AgregarCampoDecimal(panelDer, "Peso Kg:", ref y, 1m);
        _numStock = AgregarCampoEntero(panelDer, "Stock:", ref y, 25);

        var btnGuardarProd = new Button { Text = "Guardar Producto Simple", Location = new Point(10, y), Width = 220, Height = 30 };
        btnGuardarProd.Click += async (_, _) => await GuardarProductoAsync();
        panelDer.Controls.Add(btnGuardarProd);
        y += 42;

        panelDer.Controls.Add(new Label { Text = "Crear / Agregar a Kit (Composite)", Font = new Font(Font, FontStyle.Bold), Location = new Point(10, y), AutoSize = true });
        y += 24;
        _txtKitSku = AgregarCampoTexto(panelDer, "SKU Kit:", ref y, "KIT-02");
        _txtKitNombre = AgregarCampoTexto(panelDer, "Nombre Kit:", ref y, "Kit Logistico Pro");
        _numKitPrecio = AgregarCampoDecimal(panelDer, "Precio Kit:", ref y, 95m);

        panelDer.Controls.Add(new Label { Text = "Componente:", Location = new Point(10, y + 4), Width = 95 });
        _cmbCompKit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(110, y), Width = 240 };
        panelDer.Controls.Add(_cmbCompKit);
        y += 30;

        _numCompCant = AgregarCampoEntero(panelDer, "Cant. comp:", ref y, 1);

        var btnAgregarCompKit = new Button { Text = "Agregar Componente al Kit", Location = new Point(10, y), Width = 220, Height = 30 };
        btnAgregarCompKit.Click += async (_, _) => await AgregarComponenteKitAsync();
        panelDer.Controls.Add(btnAgregarCompKit);
        y += 38;

        var btnInspeccionar = new Button { Text = "Inspeccionar Elemento Seleccionado", Location = new Point(10, y), Width = 240, Height = 30 };
        btnInspeccionar.Click += async (_, _) => await InspeccionarElementoSeleccionadoAsync();
        panelDer.Controls.Add(btnInspeccionar);
        y += 36;

        _txtResumenVisitor = new TextBox { Location = new Point(10, y), Width = 440, Height = 130, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        panelDer.Controls.Add(_txtResumenVisitor);

        split.Panel2.Controls.Add(panelDer);
        page.Controls.Add(split);
        return page;
    }

    private TabPage CrearTabClientes()
    {
        var page = new TabPage("2. Clientes");
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 760 };

        _dgvClientes = CrearGridSoloLectura();
        split.Panel1.Controls.Add(CrearContenedorGrid(_dgvClientes));

        var panelDer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        int y = 10;
        panelDer.Controls.Add(new Label { Text = "Registrar Cliente", Font = new Font(Font, FontStyle.Bold), Location = new Point(10, y), AutoSize = true });
        y += 26;

        _txtCliCodigo = AgregarCampoTexto(panelDer, "Codigo:", ref y, "CLI-004");
        _txtCliNombre = AgregarCampoTexto(panelDer, "Nombre:", ref y, "Cliente Nuevo S.A.");
        _txtCliContacto = AgregarCampoTexto(panelDer, "Contacto:", ref y, "contacto@empresa.ec");

        panelDer.Controls.Add(new Label { Text = "Categoria:", Location = new Point(10, y + 4), Width = 95 });
        _cmbCliCategoria = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(110, y), Width = 220 };
        _cmbCliCategoria.Items.AddRange(new object[] { CategoriaCliente.NORMAL, CategoriaCliente.VIP, CategoriaCliente.MAYORISTA });
        _cmbCliCategoria.SelectedIndex = 0;
        panelDer.Controls.Add(_cmbCliCategoria);
        y += 34;

        var btnGuardarCli = new Button { Text = "Guardar Cliente", Location = new Point(10, y), Width = 200, Height = 32 };
        btnGuardarCli.Click += async (_, _) => await GuardarClienteAsync();
        panelDer.Controls.Add(btnGuardarCli);

        split.Panel2.Controls.Add(panelDer);
        page.Controls.Add(split);
        return page;
    }

    private TabPage CrearTabPedidos()
    {
        var page = new TabPage("3. Pedidos (Builder / Memento / Interpreter / Prototype)");
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 320 };

        var sup = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        sup.Controls.Add(new Label { Text = "Cliente:", Location = new Point(10, 14), AutoSize = true });
        _cmbPedidoCliente = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(70, 10), Width = 230 };

        sup.Controls.Add(new Label { Text = "Entrega:", Location = new Point(315, 14), AutoSize = true });
        _cmbPedidoEntrega = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(375, 10), Width = 130 };
        _cmbPedidoEntrega.Items.AddRange(new object[] { TipoEntrega.ESTANDAR, TipoEntrega.EXPRES });
        _cmbPedidoEntrega.SelectedIndex = 0;
        _cmbPedidoEntrega.SelectedIndexChanged += (_, _) =>
        {
            if (_cmbPedidoEntrega.SelectedItem is TipoEntrega te)
            {
                _coord.HistorialBorrador.GuardarEstado(_coord.Borrador);
                _coord.Borrador.TipoEntrega = te;
                RefrescarBorradorVisual();
            }
        };

        sup.Controls.Add(new Label { Text = "Item:", Location = new Point(520, 14), AutoSize = true });
        _cmbPedidoElemento = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(560, 10), Width = 240 };

        _numPedidoCant = new NumericUpDown { Location = new Point(810, 10), Width = 65, Minimum = 1, Maximum = 500, Value = 1 };

        var btnAgregarItem = new Button { Text = "Agregar al Borrador", Location = new Point(885, 9), Width = 150, Height = 26 };
        btnAgregarItem.Click += async (_, _) => await AgregarItemBorradorAsync();

        var btnDeshacer = new Button { Text = "Deshacer (Memento)", Location = new Point(10, 44), Width = 150, Height = 28 };
        btnDeshacer.Click += (_, _) =>
        {
            if (_coord.HistorialBorrador.Deshacer(_coord.Borrador))
                RefrescarBorradorVisual();
        };

        var btnRehacer = new Button { Text = "Rehacer", Location = new Point(168, 44), Width = 100, Height = 28 };
        btnRehacer.Click += (_, _) =>
        {
            if (_coord.HistorialBorrador.Rehacer(_coord.Borrador))
                RefrescarBorradorVisual();
        };

        sup.Controls.Add(new Label { Text = "Clave Idempotencia:", Location = new Point(285, 50), AutoSize = true });
        _txtClaveIdempotencia = new TextBox { Location = new Point(415, 46), Width = 190, Text = $"PAY-{DateTime.UtcNow:HHmmss}" };

        var btnConfirmar = new Button { Text = "Confirmar y Pagar Pedido", Location = new Point(620, 44), Width = 195, Height = 28, BackColor = Color.FromArgb(220, 245, 225) };
        btnConfirmar.Click += async (_, _) => await ConfirmarPedidoAsync();

        _lblTotalBorrador = new Label { Text = "Subtotal borrador: $0.00", Location = new Point(830, 50), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };

        _dgvBorrador = CrearGridSoloLectura();
        var contenedorBorrador = CrearContenedorGrid(_dgvBorrador);
        contenedorBorrador.Location = new Point(10, 82);
        contenedorBorrador.Size = new Size(1180, 220);
        contenedorBorrador.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        sup.Controls.AddRange(new Control[]
        {
            _cmbPedidoCliente, _cmbPedidoEntrega, _cmbPedidoElemento, _numPedidoCant,
            btnAgregarItem, btnDeshacer, btnRehacer, _txtClaveIdempotencia, btnConfirmar,
            _lblTotalBorrador, contenedorBorrador
        });
        split.Panel1.Controls.Add(sup);

        var inf = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        inf.Controls.Add(new Label { Text = "Filtro Interpreter (ej: estado:PAGADO total>50):", Location = new Point(10, 12), AutoSize = true });
        _txtFiltroPedidos = new TextBox { Location = new Point(295, 8), Width = 280 };

        var btnFiltrar = new Button { Text = "Filtrar", Location = new Point(585, 7), Width = 90, Height = 26 };
        btnFiltrar.Click += async (_, _) => await RefrescarPedidosAsync();

        var btnClonar = new Button { Text = "Clonar a Borrador (Prototype)", Location = new Point(685, 7), Width = 200, Height = 26 };
        btnClonar.Click += async (_, _) => await ClonarPedidoSeleccionadoAsync();

        var btnExportarTxt = new Button { Text = "Exportar Resumen TXT", Location = new Point(895, 7), Width = 190, Height = 26 };
        btnExportarTxt.Click += async (_, _) => await ExportarResumenPedidoAsync();

        _dgvPedidos = CrearGridSoloLectura();
        var contenedorPedidos = CrearContenedorGrid(_dgvPedidos);
        contenedorPedidos.Location = new Point(10, 42);
        contenedorPedidos.Size = new Size(1180, 290);
        contenedorPedidos.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        inf.Controls.AddRange(new Control[] { _txtFiltroPedidos, btnFiltrar, btnClonar, btnExportarTxt, contenedorPedidos });
        split.Panel2.Controls.Add(inf);

        page.Controls.Add(split);
        return page;
    }

    private TabPage CrearTabDespachoDevoluciones()
    {
        var page = new TabPage("4. Despacho y Devoluciones (State)");
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15) };

        panel.Controls.Add(new Label { Text = "Pedido:", Location = new Point(15, 18), AutoSize = true });
        _cmbPedidoOp = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(75, 14), Width = 340 };

        var btnDespachar = new Button { Text = "1. Despachar Pedido", Location = new Point(430, 13), Width = 160, Height = 28 };
        btnDespachar.Click += async (_, _) => await EjecutarCambioEstadoAsync("DESPACHAR");

        var btnEntregar = new Button { Text = "2. Marcar Entregado", Location = new Point(600, 13), Width = 160, Height = 28 };
        btnEntregar.Click += async (_, _) => await EjecutarCambioEstadoAsync("ENTREGAR");

        var btnCancelar = new Button { Text = "Cancelar Pedido", Location = new Point(770, 13), Width = 140, Height = 28 };
        btnCancelar.Click += async (_, _) => await EjecutarCambioEstadoAsync("CANCELAR");

        panel.Controls.Add(new Label { Text = "Motivo Devolucion:", Location = new Point(15, 64), AutoSize = true });
        _cmbMotivoDev = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(140, 60), Width = 210 };
        _cmbMotivoDev.Items.AddRange(new object[] { MotivoDevolucion.DEFECTO, MotivoDevolucion.ARREPENTIMIENTO });
        _cmbMotivoDev.SelectedIndex = 0;

        panel.Controls.Add(new Label { Text = "Observacion:", Location = new Point(365, 64), AutoSize = true });
        _txtObsDev = new TextBox { Location = new Point(450, 60), Width = 210, Text = "Revision en recepcion" };

        _chkProductoSinUso = new CheckBox { Text = "Producto sin uso", Location = new Point(670, 63), Width = 130, Checked = true };

        var btnDevolver = new Button { Text = "Registrar Devolucion", Location = new Point(810, 59), Width = 220, Height = 28 };
        btnDevolver.Click += async (_, _) => await ProcesarDevolucionAsync();

        _txtBitacoraOp = new TextBox { Location = new Point(15, 105), Size = new Size(1160, 520), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        _txtBitacoraOp.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        panel.Controls.AddRange(new Control[]
        {
            _cmbPedidoOp, btnDespachar, btnEntregar, btnCancelar,
            _cmbMotivoDev, _txtObsDev, _chkProductoSinUso, btnDevolver, _txtBitacoraOp
        });
        page.Controls.Add(panel);
        return page;
    }

    private TabPage CrearTabAuditoria()
    {
        var page = new TabPage("5. Auditoria, Inventario y Outbox");
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 320 };

        var top = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        var btnRefrescar = new Button { Text = "Refrescar Registros", Location = new Point(10, 8), Width = 160, Height = 28 };
        btnRefrescar.Click += async (_, _) => await RefrescarAuditoriaAsync();

        var btnProcesarOutbox = new Button { Text = "Procesar Eventos Outbox Pendientes", Location = new Point(180, 8), Width = 240, Height = 28 };
        btnProcesarOutbox.Click += async (_, _) => await ProcesarOutboxPendienteAsync();

        _lblOutboxPendientes = new Label { Text = "Outbox pendiente: 0", Location = new Point(440, 14), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };

        _dgvAuditoria = CrearGridSoloLectura();
        var contenedorAuditoria = CrearContenedorGrid(_dgvAuditoria);
        contenedorAuditoria.Location = new Point(10, 44);
        contenedorAuditoria.Size = new Size(1180, 260);
        contenedorAuditoria.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        top.Controls.AddRange(new Control[] { btnRefrescar, btnProcesarOutbox, _lblOutboxPendientes, contenedorAuditoria });
        split.Panel1.Controls.Add(top);

        _dgvMovimientos = CrearGridSoloLectura();
        split.Panel2.Controls.Add(CrearContenedorGrid(_dgvMovimientos));

        page.Controls.Add(split);
        return page;
    }

    private async void Form1_Load(object? sender, EventArgs e)
    {
        try
        {
            await _coord.InicializarAsync();
            await MostrarDialogoAccesoAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Error al iniciar LogiMarket 360", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task MostrarDialogoAccesoAsync()
    {
        while (!_coord.ContextoSqlite.TienePermiso(RolUsuario.ADMIN, RolUsuario.VENTAS, RolUsuario.BODEGA, RolUsuario.CONSULTA))
        {
            using var dialogo = new LoginDialog();
            if (dialogo.ShowDialog(this) != DialogResult.OK)
            {
                Close();
                return;
            }

            bool ingreso = await _coord.ContextoSqlite.IniciarSesionAsync(dialogo.NombreUsuario, dialogo.Contrasena);
            if (!ingreso)
            {
                MessageBox.Show(this, "Usuario o contraseña incorrectos.", "Acceso denegado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                continue;
            }
        }

        _tabs.Enabled = true;
        await RefrescarTodoAsync();
    }

    private async Task IniciarSesionDesdeCamposAsync()
    {
        bool ingreso = await _coord.ContextoSqlite.IniciarSesionAsync(_txtUsuario.Text, _txtContrasena.Text);
        if (!ingreso)
        {
            MessageBox.Show(this, "Usuario o contraseña incorrectos.", "Acceso denegado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _txtContrasena.Clear();
        _tabs.Enabled = true;
        await RefrescarTodoAsync();
    }

    private async Task CerrarSesionAsync()
    {
        _coord.ContextoSqlite.CerrarSesion();
        _tabs.Enabled = false;
        ActualizarBarraEstado();
        await MostrarDialogoAccesoAsync();
    }

    private Task RefrescarTodoAsync() => _mediador.NotificarOperacionActualizadaAsync();

    private void ActualizarBarraEstado()
    {
        var sesion = _coord.ContextoSqlite.UsuarioActual;
        _lblMetricas.Text = sesion is null
            ? "Sesión cerrada"
            : $"Usuario: {sesion.NombreCompleto} ({sesion.Rol}) | Pedidos sesión: {_coord.Metricas.PedidosConfirmadosSesion} | Total facturado: ${_coord.Metricas.TotalFacturadoSesionUsd:F2}";
    }

    private async Task RefrescarCatalogoAsync()
    {
        var elementos = await _coord.Catalogo.ListarTodosAsync();
        _dgvCatalogo.DataSource = elementos.Select(e => new
        {
            e.Sku,
            e.Nombre,
            Tipo = e is KitCompuesto ? "KIT" : "SIMPLE",
            MetadatoVisual = _coord.MetadatosVisuales.Obtener(e is KitCompuesto ? "KIT" : "SIMPLE").EtiquetaCorta,
            PrecioUsd = e.PrecioUnitario,
            PesoKg = e.PesoKg,
            e.StockDisponible
        }).ToList();

        _cmbCompKit.Items.Clear();
        _cmbPedidoElemento.Items.Clear();
        foreach (var el in elementos)
        {
            var item = new OpcionCombo(el.Sku, $"{el.Sku} - {el.Nombre} (${el.PrecioUnitario:F2})");
            _cmbCompKit.Items.Add(item);
            _cmbPedidoElemento.Items.Add(item);
        }

        if (_cmbCompKit.Items.Count > 0) _cmbCompKit.SelectedIndex = 0;
        if (_cmbPedidoElemento.Items.Count > 0) _cmbPedidoElemento.SelectedIndex = 0;
    }

    private async Task RefrescarClientesAsync()
    {
        var clientes = await _coord.Clientes.BuscarAsync(null);
        _dgvClientes.DataSource = clientes.Select(c => new
        {
            c.Codigo,
            c.Nombre,
            c.Contacto,
            c.Categoria,
            c.Activo
        }).ToList();

        _cmbPedidoCliente.Items.Clear();
        foreach (var c in clientes)
        {
            _cmbPedidoCliente.Items.Add(new OpcionCombo(c.Id.ToString(), $"{c.Codigo} - {c.Nombre} [{c.Categoria}]"));
        }

        if (_cmbPedidoCliente.Items.Count > 0)
            _cmbPedidoCliente.SelectedIndex = 0;
    }

    private async Task RefrescarPedidosAsync()
    {
        try
        {
            var filtrados = await _coord.FiltrarPedidosAsync(_txtFiltroPedidos.Text);
            _dgvPedidos.DataSource = filtrados.Select(p => new
            {
                p.Codigo,
                p.NombreCliente,
                p.CategoriaCliente,
                p.TipoEntrega,
                p.Estado,
                p.Subtotal,
                p.Descuento,
                p.TarifaEnvio,
                p.Total,
                Devolucion = p.TieneDevolucionRegistrada ? "SI" : "NO"
            }).ToList();

            var todos = await _coord.Pedidos.ListarTodosAsync();
            _cmbPedidoOp.Items.Clear();
            foreach (var p in todos)
            {
                _cmbPedidoOp.Items.Add(new OpcionCombo(p.Id.ToString(), $"{p.Codigo} | {p.NombreCliente} | {p.Estado} | ${p.Total:F2}"));
            }

            if (_cmbPedidoOp.Items.Count > 0)
                _cmbPedidoOp.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Filtro invalido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RefrescarAuditoriaAsync()
    {
        var logs = await _coord.AuditoriaOutbox.ListarAuditoriaAsync();
        _dgvAuditoria.DataSource = logs.Select(l => new
        {
            FechaLocal = l.FechaUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            l.Categoria,
            l.Accion,
            l.Detalle,
            l.Usuario
        }).ToList();

        var movs = await _coord.AuditoriaOutbox.ListarMovimientosInventarioAsync();
        _dgvMovimientos.DataSource = movs.Select(m => new
        {
            FechaLocal = m.FechaUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            m.Sku,
            m.Delta,
            m.StockResultante,
            m.Motivo,
            m.Referencia
        }).ToList();

        var pendientes = await _coord.AuditoriaOutbox.ObtenerEventosOutboxPendientesAsync();
        _lblOutboxPendientes.Text = $"Outbox pendiente: {pendientes.Count}";
    }

    private void RefrescarBorradorVisual()
    {
        _dgvBorrador.DataSource = _coord.Borrador.Items.Select(i => new
        {
            i.Sku,
            i.Nombre,
            i.Cantidad,
            i.PrecioUnitario,
            Subtotal = i.Cantidad * i.PrecioUnitario
        }).ToList();

        decimal total = _coord.Borrador.Items.Sum(i => i.Cantidad * i.PrecioUnitario);
        _lblTotalBorrador.Text = $"Subtotal borrador ({_coord.Borrador.TipoEntrega}): ${total:F2}";
    }

    private async Task GuardarProductoAsync()
    {
        try
        {
            var prod = new ProductoSimple(_txtSku.Text, _txtNombreProd.Text, _numPrecio.Value, _numPeso.Value, (int)_numStock.Value);
            await _coord.Administracion.GuardarProductoAsync(prod);
            await _coord.AuditoriaOutbox.RegistrarAuditoriaAsync("CATALOGO", "GUARDAR_PRODUCTO", $"SKU {prod.Sku} guardado", _coord.ContextoSqlite.UsuarioActual?.NombreUsuario ?? "sistema");
            await RefrescarTodoAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Validacion de Producto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task AgregarComponenteKitAsync()
    {
        try
        {
            if (_cmbCompKit.SelectedItem is not OpcionCombo sel)
                return;

            var componente = await _coord.Catalogo.ObtenerPorSkuAsync(sel.Clave);
            if (componente is null)
                return;

            var existente = await _coord.Catalogo.ObtenerPorSkuAsync(_txtKitSku.Text);
            KitCompuesto kit = existente as KitCompuesto
                ?? new KitCompuesto(_txtKitSku.Text, _txtKitNombre.Text, _numKitPrecio.Value);

            kit.AgregarComponente(componente, (int)_numCompCant.Value);
            await _coord.Administracion.GuardarKitAsync(kit);
            await _coord.AuditoriaOutbox.RegistrarAuditoriaAsync("CATALOGO", "ACTUALIZAR_KIT", $"Componente {componente.Sku} agregado a {kit.Sku}", _coord.ContextoSqlite.UsuarioActual?.NombreUsuario ?? "sistema");
            await RefrescarTodoAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Validacion de Kit (Composite)", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task InspeccionarElementoSeleccionadoAsync()
    {
        if (_dgvCatalogo.CurrentRow is null)
            return;

        string? sku = _dgvCatalogo.CurrentRow.Cells["Sku"].Value?.ToString();
        if (string.IsNullOrWhiteSpace(sku))
            return;

        var el = await _coord.Catalogo.ObtenerPorSkuAsync(sku);
        if (el is null)
            return;

        var desglose = new VisitanteDesgloseFisico();
        var peso = new VisitantePesoLogistico();
        if (el is KitCompuesto kit)
        {
            desglose.VisitarKit(kit, 1);
            peso.VisitarKit(kit, 1);
        }
        else if (el is ProductoSimple producto)
        {
            desglose.VisitarProducto(producto, 1);
            peso.VisitarProducto(producto, 1);
        }

        var lineas = new List<string>
        {
            $"Elemento: {el.Sku} - {el.Nombre}",
            $"Precio Unitario: ${el.PrecioUnitario:F2} | Peso: {peso.PesoTotalKg:F2} kg | Stock armado: {el.StockDisponible}",
            "Expansion real de SKUs hoja:"
        };
        foreach (var par in desglose.ObtenerDesglose())
            lineas.Add($"  - {par.Key}: {par.Value} unidad(es)");

        _txtResumenVisitor.Text = string.Join(Environment.NewLine, lineas);
    }

    private async Task GuardarClienteAsync()
    {
        try
        {
            if (_cmbCliCategoria.SelectedItem is not CategoriaCliente cat)
                return;

            var cli = new Cliente(Guid.NewGuid(), _txtCliCodigo.Text, _txtCliNombre.Text, _txtCliContacto.Text, cat, true);
            await _coord.Administracion.GuardarClienteAsync(cli);
            await _coord.AuditoriaOutbox.RegistrarAuditoriaAsync("CLIENTES", "GUARDAR_CLIENTE", $"Cliente {cli.Codigo} registrado", _coord.ContextoSqlite.UsuarioActual?.NombreUsuario ?? "sistema");
            await RefrescarTodoAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Validacion de Cliente", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task AgregarItemBorradorAsync()
    {
        if (_cmbPedidoElemento.SelectedItem is not OpcionCombo sel)
            return;

        var el = await _coord.Catalogo.ObtenerPorSkuAsync(sel.Clave);
        if (el is null)
            return;

        _coord.HistorialBorrador.GuardarEstado(_coord.Borrador);
        _coord.Borrador.AgregarOActualizarItem(el.Sku, el.Nombre, (int)_numPedidoCant.Value, el.PrecioUnitario);
        RefrescarBorradorVisual();
    }

    private async Task ConfirmarPedidoAsync()
    {
        try
        {
            if (_cmbPedidoCliente.SelectedItem is not OpcionCombo selCli)
                return;

            var cliente = await _coord.Clientes.ObtenerPorIdAsync(Guid.Parse(selCli.Clave));
            if (cliente is null)
                return;

            string codigoPedido = $"PED-{DateTime.UtcNow:yyyyMMddHHmmss}";
            var res = await _coord.ConfirmarPedidoActualAsync(cliente, codigoPedido, _txtClaveIdempotencia.Text);
            if (!res.Exitoso)
            {
                MessageBox.Show(this, res.Mensaje, "Validacion / Confirmacion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _coord.Borrador.EstablecerItems(Array.Empty<ItemBorradorMemento>());
            _txtClaveIdempotencia.Text = $"PAY-{DateTime.UtcNow:HHmmssfff}";
            await RefrescarTodoAsync();
            MessageBox.Show(this, res.Mensaje, "Pedido Confirmado", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Error al confirmar pedido", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ClonarPedidoSeleccionadoAsync()
    {
        if (_dgvPedidos.CurrentRow is null)
            return;

        string? codigo = _dgvPedidos.CurrentRow.Cells["Codigo"].Value?.ToString();
        if (string.IsNullOrWhiteSpace(codigo))
            return;

        var pedido = await _coord.Pedidos.ObtenerPorCodigoAsync(codigo);
        if (pedido is null)
            return;

        var itemsPrototipo = pedido.Lineas
            .Select(l => new ItemBorradorPrototipo(l.Sku, l.NombreProducto, l.Cantidad, l.PrecioUnitarioInstantanea))
            .ToList();

        var plantilla = new PlantillaCarritoBorrador(pedido.Codigo, pedido.ClienteId, pedido.TipoEntrega, itemsPrototipo);
        var clon = plantilla.Clonar();

        _coord.HistorialBorrador.GuardarEstado(_coord.Borrador);
        _coord.Borrador.TipoEntrega = clon.TipoEntrega;
        _coord.Borrador.EstablecerItems(clon.Items.Select(i => new ItemBorradorMemento(i.Sku, i.Nombre, i.Cantidad, i.PrecioUnitario)));

        RefrescarBorradorVisual();
        MessageBox.Show(this, $"Pedido {codigo} clonado al borrador actual (Prototype).", "Clonacion exitosa", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ExportarResumenPedidoAsync()
    {
        if (_dgvPedidos.CurrentRow is null)
            return;

        string? codigo = _dgvPedidos.CurrentRow.Cells["Codigo"].Value?.ToString();
        if (string.IsNullOrWhiteSpace(codigo))
            return;

        var pedido = await _coord.Pedidos.ObtenerPorCodigoAsync(codigo);
        if (pedido is null)
            return;

        var opcion = MessageBox.Show(this,
            "Sí: guardar comprobante TXT. No: mostrar vista previa.",
            "Salida del comprobante (Bridge)", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (opcion == DialogResult.Cancel)
            return;

        var pago = await _coord.Pedidos.ObtenerPagoPorPedidoAsync(pedido.Id);
        ISalidaDocumento salida = opcion == DialogResult.Yes
            ? _coord.SalidaComprobante
            : new SalidaTextoMemoria();
        var comprobante = new ComprobanteVenta(salida, pedido, pago?.ReferenciaPasarela ?? "PENDIENTE");
        string contenidoSalida = await comprobante.EmitirAsync();
        MessageBox.Show(this, contenidoSalida,
            opcion == DialogResult.Yes ? "Comprobante exportado" : "Vista previa del comprobante",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task EjecutarCambioEstadoAsync(string accion)
    {
        try
        {
            if (_cmbPedidoOp.SelectedItem is not OpcionCombo sel)
                return;

            Guid pedidoId = Guid.Parse(sel.Clave);
            IComandoLogiMarket comando = accion switch
            {
                "DESPACHAR" => new DespacharPedidoCommand(pedidoId, _coord.Pedidos, _coord.AuditoriaOutbox, _coord.ContextoSqlite, _coord.ContextoSqlite),
                "ENTREGAR" => new EntregarPedidoCommand(pedidoId, _coord.Pedidos, _coord.AuditoriaOutbox, _coord.ContextoSqlite, _coord.ContextoSqlite, _coord.PublicadorEventos),
                "CANCELAR" => new CancelarPedidoCommand(pedidoId, _coord.Pedidos, _coord.AuditoriaOutbox, _coord.ContextoSqlite, _coord.ContextoSqlite),
                _ => throw new ArgumentOutOfRangeException(nameof(accion), "Operacion de pedido no reconocida")
            };
            var resultado = await _coord.Invocador.EjecutarAsync(comando);
            if (!resultado.Exitoso)
            {
                MessageBox.Show(this, resultado.Mensaje, "Operacion denegada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _txtBitacoraOp.AppendText($"[{DateTime.Now:HH:mm:ss}] {accion}: {resultado.Mensaje}{Environment.NewLine}");
            await RefrescarTodoAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Transicion de estado invalida (State)", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ProcesarDevolucionAsync()
    {
        try
        {
            if (_cmbPedidoOp.SelectedItem is not OpcionCombo sel || _cmbMotivoDev.SelectedItem is not MotivoDevolucion motivo)
                return;

            var comando = new ProcesarDevolucionCommand(
                Guid.Parse(sel.Clave), motivo, _chkProductoSinUso.Checked, _txtObsDev.Text, DateTime.UtcNow,
                _coord.Pedidos, _coord.Catalogo, _coord.AuditoriaOutbox, _coord.ContextoSqlite,
                _coord.ContextoSqlite, _coord.PublicadorEventos);
            var resultado = await _coord.Invocador.EjecutarAsync(comando);
            if (!resultado.Exitoso)
            {
                MessageBox.Show(this, resultado.Mensaje, "Devolucion rechazada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _txtBitacoraOp.AppendText($"[{DateTime.Now:HH:mm:ss}] {resultado.Mensaje}{Environment.NewLine}");
            await RefrescarTodoAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Validacion de Devolucion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ProcesarOutboxPendienteAsync()
    {
        var resultado = await _coord.ProcesadorOutbox.ProcesarPendientesAsync();
        await RefrescarAuditoriaAsync();
        MessageBox.Show(this, $"Procesados: {resultado.Procesados}. Fallidos pendientes de reintento: {resultado.Fallidos}.", "Outbox Transaccional", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static DataGridView CrearGridSoloLectura() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
        ScrollBars = ScrollBars.Vertical,
        MultiSelect = false
    };

    private static TableLayoutPanel CrearContenedorGrid(DataGridView grid)
    {
        var barraHorizontal = new HScrollBar
        {
            Dock = DockStyle.Fill,
            Height = Math.Max(18, SystemInformation.HorizontalScrollBarHeight),
            Minimum = 0,
            SmallChange = 40,
            AccessibleName = "Desplazamiento horizontal"
        };

        var contenedor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        contenedor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        contenedor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        contenedor.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max(18, SystemInformation.HorizontalScrollBarHeight)));

        bool sincronizando = false;

        void ActualizarRango()
        {
            if (contenedor.IsDisposed || grid.IsDisposed || barraHorizontal.IsDisposed)
                return;

            int anchoVisible = Math.Max(1, grid.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            int anchoContenido = grid.Columns
                .Cast<DataGridViewColumn>()
                .Where(columna => !columna.Frozen)
                .Sum(columna => Math.Max(columna.Width, 260));
            int desplazamientoMaximo = Math.Max(0, anchoContenido - anchoVisible);

            sincronizando = true;
            try
            {
                barraHorizontal.LargeChange = anchoVisible;
                barraHorizontal.Maximum = desplazamientoMaximo + barraHorizontal.LargeChange - 1;
                barraHorizontal.Enabled = desplazamientoMaximo > 0;
                int valor = Math.Min(desplazamientoMaximo, grid.HorizontalScrollingOffset);
                barraHorizontal.Value = valor;
                grid.HorizontalScrollingOffset = valor;
            }
            finally
            {
                sincronizando = false;
            }
        }

        barraHorizontal.ValueChanged += (_, _) =>
        {
            if (sincronizando || !barraHorizontal.Enabled)
                return;

            sincronizando = true;
            try
            {
                grid.HorizontalScrollingOffset = barraHorizontal.Value;
            }
            finally
            {
                sincronizando = false;
            }
        };

        grid.Scroll += (_, e) =>
        {
            if (e.ScrollOrientation != ScrollOrientation.HorizontalScroll || sincronizando || !barraHorizontal.Enabled)
                return;

            int valor = Math.Clamp(grid.HorizontalScrollingOffset, barraHorizontal.Minimum, barraHorizontal.Maximum - barraHorizontal.LargeChange + 1);
            sincronizando = true;
            try
            {
                barraHorizontal.Value = valor;
            }
            finally
            {
                sincronizando = false;
            }
        };

        void AjustarColumnas()
        {
            foreach (DataGridViewColumn columna in grid.Columns)
            {
                if (!columna.Frozen && columna.Width != 260)
                    columna.Width = 260;
            }

            ActualizarRango();
        }

        grid.ColumnAdded += (_, e) => e.Column.Width = 260;
        grid.ColumnRemoved += (_, _) => ActualizarRango();
        grid.ColumnWidthChanged += (_, _) => ActualizarRango();
        grid.DataBindingComplete += (_, _) => AjustarColumnas();
        grid.SizeChanged += (_, _) => ActualizarRango();

        contenedor.Controls.Add(grid, 0, 0);
        contenedor.Controls.Add(barraHorizontal, 0, 1);
        ActualizarRango();
        return contenedor;
    }

    private static TextBox AgregarCampoTexto(Control contenedor, string etiqueta, ref int y, string valorInicial)
    {
        contenedor.Controls.Add(new Label { Text = etiqueta, Location = new Point(10, y + 4), Width = 95 });
        var txt = new TextBox { Location = new Point(110, y), Width = 240, Text = valorInicial };
        contenedor.Controls.Add(txt);
        y += 30;
        return txt;
    }

    private static NumericUpDown AgregarCampoDecimal(Control contenedor, string etiqueta, ref int y, decimal valorInicial)
    {
        contenedor.Controls.Add(new Label { Text = etiqueta, Location = new Point(10, y + 4), Width = 95 });
        var num = new NumericUpDown { Location = new Point(110, y), Width = 140, DecimalPlaces = 2, Minimum = 0.01m, Maximum = 100000m, Value = valorInicial };
        contenedor.Controls.Add(num);
        y += 30;
        return num;
    }

    private static NumericUpDown AgregarCampoEntero(Control contenedor, string etiqueta, ref int y, int valorInicial)
    {
        contenedor.Controls.Add(new Label { Text = etiqueta, Location = new Point(10, y + 4), Width = 95 });
        var num = new NumericUpDown { Location = new Point(110, y), Width = 140, Minimum = 0, Maximum = 100000, Value = valorInicial };
        contenedor.Controls.Add(num);
        y += 30;
        return num;
    }

    private sealed record OpcionCombo(string Clave, string Texto)
    {
        public override string ToString() => Texto;
    }
}
