namespace Mostlylucid.Avalonia.UITesting.Locators;

/// <summary>
/// Parses Playwright-flavoured selector strings into composable <see cref="Locator"/> trees.
///
/// Grammar (informal):
/// <code>
///   selector := function-call | atom (whitespace atom)*
///   function-call := name '(' selector ')' | 'nth' '(' integer ',' selector ')'
///   name := 'first' | 'last' | 'nth' | 'inside' | 'near'
///   atom := key '=' value
///   key  := 'name' | 'type' | 'text' | 'role' | 'testid' | 'label'
///   value := bare-word | "double-quoted" | 'single-quoted'
/// </code>
///
/// Multiple atoms compose with implicit AND (all must match the same control).
/// A bare string with no <c>=</c> and no <c>(</c> is treated as <c>name=&lt;string&gt;</c>
/// for backwards compatibility with the original Mostlylucid.Avalonia.UITesting API.
///
/// Examples:
/// <code>
///   "name=SaveBtn"
///   "type=Button text=Save"
///   "role=button name=Save"
///   "testid=save-btn"
///   "label=Email"
///   "first(type=Button)"
///   "nth(2, type=ListBoxItem)"
///   "inside(name=Header) type=TextBlock"
///   "near(name=JobList) type=Button"
///   "type=Button text='Save Resume'"
///   "type=Button:has-text(Save)"
/// </code>
/// </summary>
public static class SelectorParser
{
    public static Locator Parse(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            throw new SelectorParseException(selector ?? "", 0, "selector is empty");

        var trimmed = selector.Trim();

        // Backwards compat: bare word with no operators → name=word
        if (!trimmed.Contains('=') && !trimmed.Contains('(') && !trimmed.Contains(' '))
        {
            return new NameLocator(trimmed) { Source = selector };
        }

        var parser = new Parser(selector);
        var locator = parser.ParseSelector();
        parser.SkipWhitespace();
        if (!parser.AtEnd)
            throw new SelectorParseException(selector, parser.Position, $"unexpected '{parser.Peek()}'");

        return locator;
    }

    private sealed class Parser
    {
        private readonly string _src;
        public int Position;

        public Parser(string source) { _src = source; }

        public bool AtEnd => Position >= _src.Length;
        public char Peek() => Position < _src.Length ? _src[Position] : '\0';

        public void SkipWhitespace()
        {
            while (Position < _src.Length && char.IsWhiteSpace(_src[Position])) Position++;
        }

        public Locator ParseSelector()
        {
            SkipWhitespace();

            // Try function-call: <name>(<args>)
            var savedPos = Position;
            var ident = TryReadIdent();
            if (ident != null)
            {
                SkipWhitespace();
                if (!AtEnd && Peek() == '(')
                {
                    Position++; // consume (
                    var locator = ParseFunctionBody(ident);
                    return MaybeChain(locator);
                }
                // Not a function — rewind and parse as a series of atoms
                Position = savedPos;
            }

            // Otherwise: atom (whitespace atom)*
            var atoms = new List<Locator>();
            while (!AtEnd)
            {
                SkipWhitespace();
                if (AtEnd || Peek() == ')' || Peek() == ',') break;
                atoms.Add(ParseAtom());
            }

            if (atoms.Count == 0)
                throw new SelectorParseException(_src, Position, "expected an atom");

            // Compose: filter the first atom by all subsequent atoms (implicit AND).
            // We use FilterLocator + Resolve over (root) of subsequent atoms.
            var combined = atoms[0];
            for (var i = 1; i < atoms.Count; i++)
            {
                var rest = atoms[i];
                combined = combined.Filter(c =>
                {
                    foreach (var match in rest.Resolve(c))
                        if (ReferenceEquals(match, c)) return true;
                    return false;
                }, describe: rest.Describe());
            }

            return MaybeChain(combined);
        }

        private Locator ParseFunctionBody(string functionName)
        {
            SkipWhitespace();
            switch (functionName.ToLowerInvariant())
            {
                case "first":
                {
                    var inner = ParseSelector();
                    Expect(')');
                    return inner.First();
                }
                case "last":
                {
                    var inner = ParseSelector();
                    Expect(')');
                    return inner.Last();
                }
                case "nth":
                {
                    var nText = ReadUntil(',');
                    if (!int.TryParse(nText.Trim(), out var n))
                        throw new SelectorParseException(_src, Position, $"expected integer, got '{nText}'");
                    Expect(',');
                    var inner = ParseSelector();
                    Expect(')');
                    return inner.Nth(n);
                }
                case "inside":
                {
                    var container = ParseSelector();
                    Expect(')');
                    SkipWhitespace();
                    var inner = ParseSelector();
                    return inner.Inside(container);
                }
                case "near":
                {
                    var anchor = ParseSelector();
                    Expect(')');
                    SkipWhitespace();
                    var inner = ParseSelector();
                    return inner.Near(anchor);
                }
                default:
                    throw new SelectorParseException(_src, Position, $"unknown function '{functionName}'");
            }
        }

        private Locator ParseAtom()
        {
            var key = TryReadIdent()
                ?? throw new SelectorParseException(_src, Position, "expected key=value atom");

            SkipWhitespace();
            if (AtEnd || Peek() != '=')
                throw new SelectorParseException(_src, Position, $"expected '=' after '{key}'");
            Position++; // consume =
            SkipWhitespace();

            var value = ReadValue();

            Locator atom = key.ToLowerInvariant() switch
            {
                "name" => new NameLocator(value),
                "type" => new TypeLocator(value),
                "text" => new TextLocator(value, exact: _lastValueWasQuoted),
                "role" => new RoleLocator(value),
                "testid" => new TestIdLocator(value),
                "label" => new LabelLocator(value),
                _ => throw new SelectorParseException(_src, Position, $"unknown key '{key}'")
            };

            // Pseudo: :has-text(...)
            if (!AtEnd && Peek() == ':')
            {
                Position++; // consume :
                var pseudo = TryReadIdent() ?? throw new SelectorParseException(_src, Position, "expected pseudo name");
                if (!string.Equals(pseudo, "has-text", StringComparison.OrdinalIgnoreCase))
                    throw new SelectorParseException(_src, Position, $"unknown pseudo ':{pseudo}'");
                Expect('(');
                var (pseudoText, exact) = ReadPseudoArg();
                Expect(')');
                atom = atom.HasText(pseudoText, exact);
            }

            return atom;
        }

        private Locator MaybeChain(Locator locator)
        {
            // After a function call or composed atoms, allow trailing :has-text(...)
            SkipWhitespace();
            if (!AtEnd && Peek() == ':')
            {
                Position++;
                var pseudo = TryReadIdent() ?? throw new SelectorParseException(_src, Position, "expected pseudo name");
                if (!string.Equals(pseudo, "has-text", StringComparison.OrdinalIgnoreCase))
                    throw new SelectorParseException(_src, Position, $"unknown pseudo ':{pseudo}'");
                Expect('(');
                var (pseudoText, exact) = ReadPseudoArg();
                Expect(')');
                return locator.HasText(pseudoText, exact);
            }
            return locator;
        }

        /// <summary>
        /// Reads the argument inside a pseudo such as <c>:has-text(...)</c>. Unlike
        /// <see cref="ReadValue"/>, this allows whitespace and any character except the
        /// closing paren. A quoted form (<c>'...'</c> or <c>"..."</c>) is treated as
        /// an exact match; a bare form is a substring match.
        /// </summary>
        private (string text, bool exact) ReadPseudoArg()
        {
            SkipWhitespace();
            if (AtEnd) throw new SelectorParseException(_src, Position, "expected pseudo argument");

            var ch = Peek();
            if (ch == '"' || ch == '\'')
            {
                var quote = ch;
                Position++;
                var start = Position;
                while (Position < _src.Length && _src[Position] != quote) Position++;
                if (Position >= _src.Length)
                    throw new SelectorParseException(_src, start, "unterminated quoted pseudo argument");
                var value = _src.Substring(start, Position - start);
                Position++; // consume close quote
                SkipWhitespace();
                return (value, exact: true);
            }

            // Bare: read until matching ')', allowing whitespace inside
            var s = Position;
            while (Position < _src.Length && _src[Position] != ')') Position++;
            return (_src.Substring(s, Position - s).TrimEnd(), exact: false);
        }

        private string? TryReadIdent()
        {
            var start = Position;
            while (Position < _src.Length)
            {
                var ch = _src[Position];
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') Position++;
                else break;
            }
            return Position == start ? null : _src.Substring(start, Position - start);
        }

        private bool _lastValueWasQuoted;

        private string ReadValue()
        {
            _lastValueWasQuoted = false;
            if (AtEnd) throw new SelectorParseException(_src, Position, "expected value");

            var ch = Peek();
            if (ch == '"' || ch == '\'')
            {
                _lastValueWasQuoted = true;
                var quote = ch;
                Position++; // consume open quote
                var start = Position;
                while (Position < _src.Length && _src[Position] != quote) Position++;
                if (Position >= _src.Length)
                    throw new SelectorParseException(_src, start, $"unterminated quoted value");
                var value = _src.Substring(start, Position - start);
                Position++; // consume close quote
                return value;
            }

            // Bare value: until whitespace, ')', ',' or ':'
            var s = Position;
            while (Position < _src.Length)
            {
                var c = _src[Position];
                if (char.IsWhiteSpace(c) || c == ')' || c == ',' || c == ':') break;
                Position++;
            }
            return _src.Substring(s, Position - s);
        }

        private string ReadUntil(char terminator)
        {
            var s = Position;
            while (Position < _src.Length && _src[Position] != terminator) Position++;
            return _src.Substring(s, Position - s);
        }

        private void Expect(char ch)
        {
            SkipWhitespace();
            if (AtEnd || _src[Position] != ch)
                throw new SelectorParseException(_src, Position, $"expected '{ch}'");
            Position++;
        }
    }
}
