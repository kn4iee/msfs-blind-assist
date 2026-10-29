using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// A cockpit lamp's state rule, in the cockpit's own RPN (copied from its emissive code by <c>tools/a350-gen</c>):
/// L: variables, numbers, the operators the A350's lamps use (<c>! and or == != &gt; &lt; * /</c>), and the
/// <c>if{ … } els{ … }</c> blocks some write through register 0 (<c>sp0</c>, <c>l0</c>). Lit is a value of 0.5 or
/// more, as the cockpit's own brightness reaches full. Pure.
/// </summary>
public sealed class A350LampRule
{
    private static readonly Regex Token = new(@"\(L:(?<n>[^,)]+?)(?:,\s*[^)]+?)?\)|(?<op>\S+)", RegexOptions.CultureInvariant);

    private abstract record Step;
    private sealed record Read(string Name) : Step;
    private sealed record Push(double Value) : Step;
    private sealed record Op(string Name) : Step;
    private sealed record Branch(IReadOnlyList<Step> Then, IReadOnlyList<Step> Else) : Step;

    private readonly IReadOnlyList<Step> _program;

    private A350LampRule(IReadOnlyList<Step> program, IReadOnlyList<string> inputs, string source)
    {
        _program = program;
        Inputs = inputs;
        Source = source;
    }

    /// <summary>Every L:var the rule reads (bare names, units dropped), once each, in the order it first reads them.</summary>
    public IReadOnlyList<string> Inputs { get; }

    /// <summary>The rule as the cockpit writes it: two lamps with the same source are lit by one rule, one light.</summary>
    public string Source { get; }

    /// <summary>Parses a rule; a form the cockpit does not use throws <see cref="FormatException"/>.</summary>
    public static A350LampRule Parse(string state)
    {
        var tokens = Token.Matches(state).Cast<Match>().ToList();
        var inputs = new List<string>();
        int at = 0;
        var program = Block(tokens, ref at, inputs, state, out bool closed);
        if (closed)
            throw new FormatException($"A350 lamp rule: an unopened '}}' ({state})");
        if (Depth(program, 0, state) != 1)
            throw new FormatException($"A350 lamp rule: does not leave one value ({state})");
        return new A350LampRule(program, inputs, state.Trim());
    }

    /// <summary>Reads steps up to a closing brace (<paramref name="closed"/>) or the end.</summary>
    private static List<Step> Block(List<Match> tokens, ref int at, List<string> inputs, string state, out bool closed)
    {
        var steps = new List<Step>();
        while (at < tokens.Count)
        {
            var m = tokens[at++];
            if (m.Groups["n"].Success)
            {
                string name = m.Groups["n"].Value.Trim();
                if (!inputs.Contains(name))
                    inputs.Add(name);
                steps.Add(new Read(name));
                continue;
            }
            string op = m.Groups["op"].Value;
            if (op == "}")
            {
                closed = true;
                return steps;
            }
            if (op == "if{")
            {
                var then = Block(tokens, ref at, inputs, state, out bool thenClosed);
                if (!thenClosed || at >= tokens.Count || tokens[at].Groups["op"].Value != "els{")
                    throw new FormatException($"A350 lamp rule: an if{{ with no els{{ ({state})");
                at++;
                var otherwise = Block(tokens, ref at, inputs, state, out bool elseClosed);
                if (!elseClosed)
                    throw new FormatException($"A350 lamp rule: an unclosed els{{ ({state})");
                steps.Add(new Branch(then, otherwise));
                continue;
            }
            if (double.TryParse(op, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                steps.Add(new Push(number));
                continue;
            }
            if (op is not ("!" or "and" or "or" or "==" or "!=" or ">" or "<" or "*" or "/" or "sp0" or "l0"))
                throw new FormatException($"A350 lamp rule: '{op}' is not a form the cockpit uses ({state})");
            steps.Add(new Op(op));
        }
        closed = false;
        return steps;
    }

    /// <summary>The stack depth a block leaves from <paramref name="depth"/>; both branches of an if must agree.</summary>
    private static int Depth(IReadOnlyList<Step> steps, int depth, string state)
    {
        foreach (var step in steps)
        {
            int needs = 0, leaves = 1;
            switch (step)
            {
                case Op { Name: "!" }: needs = 1; break;
                case Op { Name: "sp0" }: needs = 1; leaves = 0; break;
                case Op { Name: "l0" }: break;
                case Op: needs = 2; break;
                case Branch b:
                    if (depth < 1)
                        throw new FormatException($"A350 lamp rule: an if{{ with no condition ({state})");
                    int then = Depth(b.Then, depth - 1, state), otherwise = Depth(b.Else, depth - 1, state);
                    if (then != otherwise)
                        throw new FormatException($"A350 lamp rule: branches leave different depths ({state})");
                    depth = then;
                    continue;
            }
            if (depth < needs)
                throw new FormatException($"A350 lamp rule: an operator has too few values ({state})");
            depth += leaves - needs;
        }
        return depth;
    }

    /// <summary>The rule's value, or null while an input it reads on the path taken is unread.</summary>
    public double? Evaluate(Func<string, double?> read)
    {
        var stack = new Stack<double>();
        double register = 0;
        return Run(_program, stack, ref register, read) ? stack.Pop() : null;
    }

    private static bool Run(IReadOnlyList<Step> steps, Stack<double> stack, ref double register, Func<string, double?> read)
    {
        foreach (var step in steps)
        {
            switch (step)
            {
                case Read r:
                    if (read(r.Name) is not double value)
                        return false;
                    stack.Push(value);
                    break;
                case Push p:
                    stack.Push(p.Value);
                    break;
                case Branch b:
                    if (!Run(stack.Pop() != 0 ? b.Then : b.Else, stack, ref register, read))
                        return false;
                    break;
                case Op { Name: "!" }:
                    stack.Push(stack.Pop() == 0 ? 1 : 0);
                    break;
                case Op { Name: "sp0" }:
                    register = stack.Pop();
                    break;
                case Op { Name: "l0" }:
                    stack.Push(register);
                    break;
                case Op o:
                    double b2 = stack.Pop(), a = stack.Pop();
                    stack.Push(o.Name switch
                    {
                        "and" => a != 0 && b2 != 0 ? 1 : 0,
                        "or" => a != 0 || b2 != 0 ? 1 : 0,
                        "==" => Math.Abs(a - b2) < 1e-6 ? 1 : 0,
                        "!=" => Math.Abs(a - b2) < 1e-6 ? 0 : 1,
                        ">" => a > b2 ? 1 : 0,
                        "<" => a < b2 ? 1 : 0,
                        "*" => a * b2,
                        _ => b2 == 0 ? 0 : a / b2,
                    });
                    break;
            }
        }
        return true;
    }

    /// <summary>Whether the state lights the lamp (power aside), or null while an input is unread.</summary>
    public bool? IsLit(Func<string, double?> read) => Evaluate(read) is double v ? v >= 0.5 : null;
}
