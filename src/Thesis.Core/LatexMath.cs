namespace Thesis.Core;

public abstract record MathNode;
public sealed record MathText(string Value) : MathNode;
public sealed record MathSequence(IReadOnlyList<MathNode> Children) : MathNode;
public sealed record MathFraction(MathNode Numerator, MathNode Denominator) : MathNode;
public sealed record MathRadical(MathNode Value, MathNode? Degree = null) : MathNode;
public sealed record MathScript(MathNode Value, MathNode? Superscript, MathNode? Subscript) : MathNode;

/// <summary>A bounded LaTeX math subset. Unknown commands stay visible and the project keeps the original source.</summary>
public static class LatexMath
{
    public static MathNode Parse(string source)
    {
        if (source.Length > 16000) throw new InvalidDataException("公式超过 16000 字符。");
        if (source.Any(character => char.IsControl(character) && character is not ('\t' or '\n' or '\r')))
            throw new InvalidDataException("公式包含 XML 不支持的控制字符。");
        var parser = new Parser(source); return parser.ReadSequence();
    }

    private sealed class Parser(string source)
    {
        private int position;
        private int depth;
        private static readonly Dictionary<string, string> Symbols = new(StringComparer.Ordinal)
        {
            ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ", ["epsilon"] = "ε", ["varepsilon"] = "ϵ",
            ["zeta"] = "ζ", ["eta"] = "η", ["theta"] = "θ", ["vartheta"] = "ϑ", ["iota"] = "ι", ["kappa"] = "κ",
            ["lambda"] = "λ", ["mu"] = "μ", ["nu"] = "ν", ["xi"] = "ξ", ["pi"] = "π", ["rho"] = "ρ",
            ["sigma"] = "σ", ["tau"] = "τ", ["upsilon"] = "υ", ["phi"] = "φ", ["varphi"] = "ϕ", ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω",
            ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ", ["Xi"] = "Ξ", ["Pi"] = "Π", ["Sigma"] = "Σ", ["Phi"] = "Φ", ["Psi"] = "Ψ", ["Omega"] = "Ω",
            ["sum"] = "∑", ["prod"] = "∏", ["int"] = "∫", ["iint"] = "∬", ["oint"] = "∮", ["infty"] = "∞", ["partial"] = "∂", ["nabla"] = "∇",
            ["times"] = "×", ["cdot"] = "·", ["div"] = "÷", ["pm"] = "±", ["mp"] = "∓", ["le"] = "≤", ["leq"] = "≤", ["ge"] = "≥", ["geq"] = "≥", ["neq"] = "≠", ["approx"] = "≈", ["equiv"] = "≡",
            ["to"] = "→", ["rightarrow"] = "→", ["leftarrow"] = "←", ["Rightarrow"] = "⇒", ["leftrightarrow"] = "↔", ["in"] = "∈", ["notin"] = "∉", ["subset"] = "⊂", ["subseteq"] = "⊆", ["cup"] = "∪", ["cap"] = "∩", ["emptyset"] = "∅",
            ["forall"] = "∀", ["exists"] = "∃", ["ldots"] = "…", ["cdots"] = "⋯", ["vert"] = "|", ["Vert"] = "‖", ["langle"] = "⟨", ["rangle"] = "⟩",
            ["sin"] = "sin", ["cos"] = "cos", ["tan"] = "tan", ["log"] = "log", ["ln"] = "ln", ["exp"] = "exp", ["lim"] = "lim", ["max"] = "max", ["min"] = "min"
        };

        public MathNode ReadSequence(char? end = null)
        {
            if (++depth > 64) throw new InvalidDataException("公式嵌套超过 64 层。");
            var nodes = new List<MathNode>();
            while (position < source.Length && source[position] != end)
            {
                if (source[position] == '}') throw Error("多余的右花括号");
                if (char.IsWhiteSpace(source[position])) { position++; continue; }
                var node = ReadAtom(); MathNode? sup = null, sub = null;
                while (position < source.Length)
                {
                    while (position < source.Length && char.IsWhiteSpace(source[position])) position++;
                    if (position >= source.Length || source[position] is not ('^' or '_')) break;
                    var script = source[position++]; var value = ReadArgument();
                    if (script == '^') { if (sup is not null) throw Error("重复上标"); sup = value; }
                    else { if (sub is not null) throw Error("重复下标"); sub = value; }
                }
                nodes.Add(sup is not null || sub is not null ? new MathScript(node, sup, sub) : node);
            }
            if (end is not null) { if (position >= source.Length) throw Error("缺少右括号"); position++; }
            depth--; return nodes.Count == 1 ? nodes[0] : new MathSequence(nodes);
        }

        private MathNode ReadArgument()
        {
            if (++depth > 64) throw new InvalidDataException("公式嵌套超过 64 层。");
            while (position < source.Length && char.IsWhiteSpace(source[position])) position++;
            if (position >= source.Length) throw Error("缺少公式参数");
            var result = ReadAtom(); depth--; return result;
        }

        private MathNode ReadAtom()
        {
            var c = source[position++];
            if (c == '{') return ReadSequence('}');
            if (c is '^' or '_') throw Error("上下标缺少主体");
            if (c != '\\')
            {
                var length = char.IsHighSurrogate(c) && position < source.Length && char.IsLowSurrogate(source[position]) ? 2 : 1;
                if (length == 2) position++;
                return new MathText(source.Substring(position - length, length));
            }
            if (position == source.Length) return new MathText("\\");
            var start = position;
            if (!char.IsLetter(source[position]))
            {
                var escaped = source[position++];
                return new MathText(escaped switch { ',' or ':' or ';' or ' ' => " ", '!' => "", _ => escaped.ToString() });
            }
            while (position < source.Length && char.IsLetter(source[position])) position++;
            var command = source[start..position];
            if (command is "frac" or "dfrac" or "tfrac") return new MathFraction(ReadArgument(), ReadArgument());
            if (command == "sqrt")
            {
                while (position < source.Length && char.IsWhiteSpace(source[position])) position++;
                MathNode? degree = null;
                if (position < source.Length && source[position] == '[') { position++; degree = ReadSequence(']'); }
                return new MathRadical(ReadArgument(), degree);
            }
            if (command is "left" or "right" or "displaystyle" or "textstyle" or "limits" or "nolimits") return new MathText("");
            if (command is "quad" or "qquad") return new MathText(command == "quad" ? "　" : "　　");
            if (command is "text" or "mathrm" or "operatorname")
            {
                while (position < source.Length && char.IsWhiteSpace(source[position])) position++;
                if (position < source.Length && source[position] == '{')
                {
                    var begin = ++position; var nesting = 1;
                    while (position < source.Length && nesting > 0) { var next = source[position++]; if (next == '{') nesting++; if (next == '}') nesting--; }
                    if (nesting > 0) throw Error("文本缺少右花括号");
                    return new MathText(source[begin..(position - 1)]);
                }
            }
            return new MathText(Symbols.TryGetValue(command, out var symbol) ? symbol : "\\" + command);
        }
        private InvalidDataException Error(string message) => new($"{message}（位置 {position}）。");
    }
}
