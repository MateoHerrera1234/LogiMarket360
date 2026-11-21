using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LogiMarket.Domain.Enums;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Application.Comportamentales;

// Contrato de expresion del patron Interpreter para evaluar pedidos en memoria sin concatenar SQL
public interface IExpresionFiltroPedido
{
    bool Evaluar(Pedido pedido);
}

public sealed class ExpresionEstadoPedido : IExpresionFiltroPedido
{
    private readonly EstadoPedido _estadoEsperado;

    public ExpresionEstadoPedido(string valorEstado)
    {
        if (!Enum.TryParse<EstadoPedido>(valorEstado.Trim(), ignoreCase: true, out var estado))
            throw new ArgumentException($"Estado de filtro no valido: {valorEstado}");

        _estadoEsperado = estado;
    }

    public bool Evaluar(Pedido pedido) => pedido.Estado == _estadoEsperado;
}

public sealed class ExpresionTotalPedido : IExpresionFiltroPedido
{
    private readonly string _operador;
    private readonly decimal _valor;

    public ExpresionTotalPedido(string operador, string valorTexto)
    {
        _operador = operador;
        if (!decimal.TryParse(valorTexto, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) &&
            !decimal.TryParse(valorTexto, NumberStyles.Number, CultureInfo.CurrentCulture, out parsed))
        {
            throw new ArgumentException($"Valor numerico invalido en filtro TOTAL: {valorTexto}");
        }

        _valor = parsed;
    }

    public bool Evaluar(Pedido pedido) => _operador switch
    {
        ">=" => pedido.Total >= _valor,
        "<=" => pedido.Total <= _valor,
        ">" => pedido.Total > _valor,
        "<" => pedido.Total < _valor,
        "=" or "==" => pedido.Total == _valor,
        "!=" => pedido.Total != _valor,
        _ => throw new InvalidOperationException($"Operador no soportado en TOTAL: {_operador}")
    };
}

public sealed class ExpresionClientePedido : IExpresionFiltroPedido
{
    private readonly string _textoBuscado;

    public ExpresionClientePedido(string textoBuscado)
    {
        _textoBuscado = textoBuscado.Trim();
    }

    public bool Evaluar(Pedido pedido) =>
        pedido.NombreCliente.Contains(_textoBuscado, StringComparison.OrdinalIgnoreCase);
}

public sealed class ExpresionAndPedido : IExpresionFiltroPedido
{
    private readonly IExpresionFiltroPedido _izquierda;
    private readonly IExpresionFiltroPedido _derecha;

    public ExpresionAndPedido(IExpresionFiltroPedido izquierda, IExpresionFiltroPedido derecha)
    {
        _izquierda = izquierda;
        _derecha = derecha;
    }

    public bool Evaluar(Pedido pedido) => _izquierda.Evaluar(pedido) && _derecha.Evaluar(pedido);
}

public sealed class ExpresionOrPedido : IExpresionFiltroPedido
{
    private readonly IExpresionFiltroPedido _izquierda;
    private readonly IExpresionFiltroPedido _derecha;

    public ExpresionOrPedido(IExpresionFiltroPedido izquierda, IExpresionFiltroPedido derecha)
    {
        _izquierda = izquierda;
        _derecha = derecha;
    }

    public bool Evaluar(Pedido pedido) => _izquierda.Evaluar(pedido) || _derecha.Evaluar(pedido);
}

// Parser de expresiones: ESTADO = valor, TOTAL >= numero, CLIENTE = texto con AND, OR y parentesis
public sealed class ParserFiltroPedidos
{
    private readonly List<string> _tokens;
    private int _posicion;

    private ParserFiltroPedidos(List<string> tokens)
    {
        _tokens = tokens;
    }

    public static IExpresionFiltroPedido Parsear(string? expresion)
    {
        if (string.IsNullOrWhiteSpace(expresion))
            return new ExpresionSiempreVerdadera();

        var tokens = Tokenizar(expresion);
        var parser = new ParserFiltroPedidos(tokens);
        var raiz = parser.ParsearOr();
        if (parser._posicion < parser._tokens.Count)
            throw new FormatException($"Token inesperado cerca de '{parser._tokens[parser._posicion]}'");

        return raiz;
    }

    private IExpresionFiltroPedido ParsearOr()
    {
        var expr = ParsearAnd();
        while (_posicion < _tokens.Count &&
               string.Equals(_tokens[_posicion], "OR", StringComparison.OrdinalIgnoreCase))
        {
            _posicion++;
            var derecha = ParsearAnd();
            expr = new ExpresionOrPedido(expr, derecha);
        }

        return expr;
    }

    private IExpresionFiltroPedido ParsearAnd()
    {
        var expr = ParsearPrimario();
        while (_posicion < _tokens.Count &&
               string.Equals(_tokens[_posicion], "AND", StringComparison.OrdinalIgnoreCase))
        {
            _posicion++;
            var derecha = ParsearPrimario();
            expr = new ExpresionAndPedido(expr, derecha);
        }

        return expr;
    }

    private IExpresionFiltroPedido ParsearPrimario()
    {
        if (_posicion >= _tokens.Count)
            throw new FormatException("Expresion de filtro incompleta");

        if (_tokens[_posicion] == "(")
        {
            _posicion++;
            var interna = ParsearOr();
            if (_posicion >= _tokens.Count || _tokens[_posicion] != ")")
                throw new FormatException("Falta parentesis de cierre ')'");
            _posicion++;
            return interna;
        }

        if (_posicion + 2 >= _tokens.Count)
            throw new FormatException("Condicion de filtro incompleta (se espera CAMPO OPERADOR VALOR)");

        string campo = _tokens[_posicion++].ToUpperInvariant();
        string operador = _tokens[_posicion++];
        string valor = _tokens[_posicion++];

        return campo switch
        {
            "ESTADO" when operador == "=" => new ExpresionEstadoPedido(valor),
            "TOTAL" => new ExpresionTotalPedido(operador, valor),
            "CLIENTE" when operador == "=" => new ExpresionClientePedido(valor),
            _ => throw new FormatException($"Condicion no soportada: {campo} {operador} {valor}")
        };
    }

    private static List<string> Tokenizar(string texto)
    {
        var tokens = new List<string>();
        var actual = new StringBuilder();
        bool enComillas = false;

        for (int i = 0; i < texto.Length; i++)
        {
            char c = texto[i];
            if (c == '"')
            {
                enComillas = !enComillas;
                continue;
            }

            if (!enComillas && (c == '(' || c == ')'))
            {
                if (actual.Length > 0)
                {
                    tokens.Add(actual.ToString().Trim());
                    actual.Clear();
                }
                tokens.Add(c.ToString());
                continue;
            }

            if (!enComillas && char.IsWhiteSpace(c))
            {
                if (actual.Length > 0)
                {
                    tokens.Add(actual.ToString().Trim());
                    actual.Clear();
                }
                continue;
            }

            actual.Append(c);
        }

        if (actual.Length > 0)
            tokens.Add(actual.ToString().Trim());

        return tokens;
    }

    private sealed class ExpresionSiempreVerdadera : IExpresionFiltroPedido
    {
        public bool Evaluar(Pedido pedido) => true;
    }
}
