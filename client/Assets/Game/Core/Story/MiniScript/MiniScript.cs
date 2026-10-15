using System;
using System.Collections.Generic;
using System.Text;
using Game.Domain;

namespace Game.Core.Story.MiniScript
{
    public abstract class MiniScriptInstruction
    {
        public int LineNumber { get; }

        protected MiniScriptInstruction(int lineNumber)
        {
            LineNumber = lineNumber;
        }
    }

    public sealed class LineInstruction : MiniScriptInstruction
    {
        public string LineId { get; }
        public string Speaker { get; }
        public string TextKey { get; }

        public LineInstruction(string lineId, string speaker, string textKey, int lineNumber)
            : base(lineNumber)
        {
            LineId = lineId ?? string.Empty;
            Speaker = speaker ?? string.Empty;
            TextKey = textKey ?? string.Empty;
        }
    }

    public sealed class OptionInstruction : MiniScriptInstruction
    {
        public string TextKey { get; }
        public string Condition { get; }
        public string TargetNode { get; }
        public bool IsSelectable { get; }

        public OptionInstruction(string textKey, string condition, string targetNode, bool isSelectable, int lineNumber)
            : base(lineNumber)
        {
            TextKey = textKey ?? string.Empty;
            Condition = condition;
            TargetNode = targetNode ?? string.Empty;
            IsSelectable = isSelectable;
        }
    }

    public sealed class SetInstruction : MiniScriptInstruction
    {
        public string VariableName { get; }
        public string Expression { get; }

        public SetInstruction(string variableName, string expression, int lineNumber)
            : base(lineNumber)
        {
            VariableName = variableName ?? string.Empty;
            Expression = expression ?? string.Empty;
        }
    }

    public sealed class IfInstruction : MiniScriptInstruction
    {
        public string Condition { get; }
        public string TargetNode { get; }

        public IfInstruction(string condition, string targetNode, int lineNumber)
            : base(lineNumber)
        {
            Condition = condition ?? string.Empty;
            TargetNode = targetNode ?? string.Empty;
        }
    }

    public sealed class JumpInstruction : MiniScriptInstruction
    {
        public string TargetNode { get; }

        public JumpInstruction(string targetNode, int lineNumber)
            : base(lineNumber)
        {
            TargetNode = targetNode ?? string.Empty;
        }
    }

    public sealed class CommandInstruction : MiniScriptInstruction
    {
        public string CommandName { get; }
        public string[] Arguments { get; }

        public CommandInstruction(string commandName, string[] arguments, int lineNumber)
            : base(lineNumber)
        {
            CommandName = commandName ?? string.Empty;
            Arguments = arguments ?? Array.Empty<string>();
        }
    }

    public sealed class EndInstruction : MiniScriptInstruction
    {
        public EndInstruction(int lineNumber) : base(lineNumber) { }
    }

    public sealed class MiniScriptNode
    {
        public string Name { get; }
        public Rating Rating { get; set; } = Rating.G;
        public string FallbackNode { get; set; }
        public List<string> Tags { get; } = new List<string>();
        public List<MiniScriptInstruction> Instructions { get; } = new List<MiniScriptInstruction>();

        public MiniScriptNode(string name)
        {
            Name = name ?? string.Empty;
        }
    }

    /// <summary>
    /// 解析後的 MiniScript 腳本物件，包含節點集合。
    /// </summary>
    public sealed class MiniScript
    {
        private readonly Dictionary<string, MiniScriptNode> _nodes;

        public IReadOnlyDictionary<string, MiniScriptNode> Nodes => _nodes;

        public MiniScript(Dictionary<string, MiniScriptNode> nodes)
        {
            _nodes = nodes ?? new Dictionary<string, MiniScriptNode>(StringComparer.Ordinal);
        }

        public static MiniScript Parse(string scriptText)
        {
            if (scriptText == null) throw new ArgumentNullException(nameof(scriptText));

            var nodes = new Dictionary<string, MiniScriptNode>(StringComparer.Ordinal);
            MiniScriptNode currentNode = null;

            string[] lines = scriptText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            for (int i = 0; i < lines.Length; i++)
            {
                int lineNumber = i + 1;
                string rawLine = lines[i];
                string trimmed = rawLine.Trim();

                if (string.IsNullOrEmpty(trimmed))
                    continue;

                if (trimmed.StartsWith("#"))
                    continue;

                // Node header: [node NodeName [tag1 tag2...]]
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    string inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    if (!inner.StartsWith("node", StringComparison.Ordinal) ||
                        (inner.Length > 4 && !char.IsWhiteSpace(inner[4])))
                    {
                        throw new FormatException($"Line {lineNumber}: Invalid node header '{trimmed}'. Expected '[node NodeName]'.");
                    }

                    string rest = inner.Substring(4).Trim();
                    string[] tokens = SplitCommandLine(rest);
                    if (tokens.Length == 0 || string.IsNullOrEmpty(tokens[0]))
                    {
                        throw new FormatException($"Line {lineNumber}: Missing node name in '{trimmed}'.");
                    }

                    string nodeName = tokens[0];
                    if (nodes.ContainsKey(nodeName))
                    {
                        throw new FormatException($"Line {lineNumber}: Duplicate node name '{nodeName}'.");
                    }

                    currentNode = new MiniScriptNode(nodeName);
                    for (int t = 1; t < tokens.Length; t++)
                    {
                        ApplyTag(currentNode, tokens[t]);
                    }

                    nodes[nodeName] = currentNode;
                    continue;
                }

                if (currentNode == null)
                {
                    throw new FormatException($"Line {lineNumber}: Instruction '{trimmed}' found outside of any [node NodeName] block.");
                }

                // line <lineId> <speaker> <textKey>
                if (trimmed.StartsWith("line ", StringComparison.Ordinal) || trimmed == "line")
                {
                    string rest = trimmed.Length > 4 ? trimmed.Substring(4).Trim() : string.Empty;
                    string[] parts = SplitCommandLine(rest);
                    if (parts.Length != 3)
                    {
                        throw new FormatException($"Line {lineNumber}: Malformed line instruction. Expected exactly 3 arguments (lineId speaker textKey), found {parts.Length}.");
                    }

                    currentNode.Instructions.Add(new LineInstruction(parts[0], parts[1], parts[2], lineNumber));
                    continue;
                }

                // option <textKey> [if <cond>] -> <targetNode> [disabled]
                if (trimmed.StartsWith("option ", StringComparison.Ordinal) || trimmed == "option")
                {
                    string rest = trimmed.Length > 6 ? trimmed.Substring(6).Trim() : string.Empty;
                    int arrowIdx = rest.IndexOf("->", StringComparison.Ordinal);
                    if (arrowIdx < 0)
                    {
                        throw new FormatException($"Line {lineNumber}: Malformed option instruction: missing '->' in '{trimmed}'.");
                    }

                    string left = rest.Substring(0, arrowIdx).Trim();
                    string right = rest.Substring(arrowIdx + 2).Trim();

                    bool isSelectable = true;

                    // Check disabled on right
                    if (right.EndsWith("[disabled]", StringComparison.OrdinalIgnoreCase))
                    {
                        isSelectable = false;
                        right = right.Substring(0, right.Length - 10).Trim();
                    }
                    // Check disabled on left
                    if (left.Contains("[disabled]"))
                    {
                        isSelectable = false;
                        left = left.Replace("[disabled]", "").Trim();
                    }
                    else if (left.EndsWith(" disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        isSelectable = false;
                        left = left.Substring(0, left.Length - 9).Trim();
                    }

                    string targetNode = right;
                    if (string.IsNullOrEmpty(targetNode))
                    {
                        throw new FormatException($"Line {lineNumber}: Malformed option instruction: missing target node in '{trimmed}'.");
                    }

                    string textKey;
                    string condition = null;
                    int ifIdx = IndexOfWord(left, "if");
                    if (ifIdx >= 0)
                    {
                        textKey = left.Substring(0, ifIdx).Trim();
                        condition = left.Substring(ifIdx + 2).Trim();
                        if (string.IsNullOrEmpty(condition))
                        {
                            throw new FormatException($"Line {lineNumber}: Malformed option instruction: empty condition after 'if'.");
                        }
                    }
                    else
                    {
                        textKey = left.Trim();
                    }

                    if (string.IsNullOrEmpty(textKey))
                    {
                        throw new FormatException($"Line {lineNumber}: Malformed option instruction: missing text key in '{trimmed}'.");
                    }

                    currentNode.Instructions.Add(new OptionInstruction(textKey, condition, targetNode, isSelectable, lineNumber));
                    continue;
                }

                // set <name> = <value>
                if (trimmed.StartsWith("set ", StringComparison.Ordinal) || trimmed == "set")
                {
                    string rest = trimmed.Length > 3 ? trimmed.Substring(3).Trim() : string.Empty;
                    int eqIdx = rest.IndexOf('=');
                    if (eqIdx < 0)
                    {
                        throw new FormatException($"Line {lineNumber}: Malformed set instruction: missing '=' in '{trimmed}'.");
                    }

                    string varName = rest.Substring(0, eqIdx).Trim();
                    string expr = rest.Substring(eqIdx + 1).Trim();

                    if (string.IsNullOrEmpty(varName))
                    {
                        throw new FormatException($"Line {lineNumber}: Malformed set instruction: missing variable name in '{trimmed}'.");
                    }
                    if (string.IsNullOrEmpty(expr))
                    {
                        throw new FormatException($"Line {lineNumber}: Malformed set instruction: missing expression in '{trimmed}'.");
                    }

                    currentNode.Instructions.Add(new SetInstruction(varName, expr, lineNumber));
                    continue;
                }

                // if <condition> -> <targetNode>
                if (trimmed.StartsWith("if ", StringComparison.Ordinal) || trimmed == "if")
                {
                    string rest = trimmed.Length > 2 ? trimmed.Substring(2).Trim() : string.Empty;
                    int arrowIdx = rest.IndexOf("->", StringComparison.Ordinal);
                    if (arrowIdx < 0)
                    {
                        throw new FormatException($"Line {lineNumber}: Malformed if instruction: missing '->' in '{trimmed}'.");
                    }

                    string condition = rest.Substring(0, arrowIdx).Trim();
                    string targetNode = rest.Substring(arrowIdx + 2).Trim();

                    if (string.IsNullOrEmpty(condition))
                    {
                        throw new FormatException($"Line {lineNumber}: Malformed if instruction: missing condition in '{trimmed}'.");
                    }
                    if (string.IsNullOrEmpty(targetNode))
                    {
                        throw new FormatException($"Line {lineNumber}: Malformed if instruction: missing target node in '{trimmed}'.");
                    }

                    currentNode.Instructions.Add(new IfInstruction(condition, targetNode, lineNumber));
                    continue;
                }

                // jump <targetNode>
                if (trimmed.StartsWith("jump ", StringComparison.Ordinal) || trimmed == "jump")
                {
                    string targetNode = trimmed.Length > 4 ? trimmed.Substring(4).Trim() : string.Empty;
                    if (string.IsNullOrEmpty(targetNode))
                    {
                        throw new FormatException($"Line {lineNumber}: Malformed jump instruction: missing target node in '{trimmed}'.");
                    }

                    currentNode.Instructions.Add(new JumpInstruction(targetNode, lineNumber));
                    continue;
                }

                // command <name> [args...]
                if (trimmed.StartsWith("command ", StringComparison.Ordinal) || trimmed == "command")
                {
                    string rest = trimmed.Length > 7 ? trimmed.Substring(7).Trim() : string.Empty;
                    string[] parts = SplitCommandLine(rest);
                    if (parts.Length == 0 || string.IsNullOrEmpty(parts[0]))
                    {
                        throw new FormatException($"Line {lineNumber}: Malformed command instruction: missing command name in '{trimmed}'.");
                    }

                    string cmdName = parts[0];
                    string[] cmdArgs = new string[parts.Length - 1];
                    Array.Copy(parts, 1, cmdArgs, 0, cmdArgs.Length);

                    currentNode.Instructions.Add(new CommandInstruction(cmdName, cmdArgs, lineNumber));
                    continue;
                }

                // tags <tag1> [tag2...]
                if (trimmed.StartsWith("tags ", StringComparison.Ordinal) || trimmed == "tags")
                {
                    string rest = trimmed.Length > 4 ? trimmed.Substring(4).Trim() : string.Empty;
                    string[] tags = SplitCommandLine(rest);
                    foreach (string t in tags)
                    {
                        if (!string.IsNullOrEmpty(t))
                        {
                            ApplyTag(currentNode, t);
                        }
                    }
                    continue;
                }

                // end
                if (trimmed == "end")
                {
                    currentNode.Instructions.Add(new EndInstruction(lineNumber));
                    continue;
                }

                throw new FormatException($"Line {lineNumber}: Unknown instruction '{trimmed}'.");
            }

            return new MiniScript(nodes);
        }

        private static void ApplyTag(MiniScriptNode node, string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;
            node.Tags.Add(tag);
            if (tag.StartsWith("rating:", StringComparison.OrdinalIgnoreCase))
            {
                string rStr = tag.Substring(7).Trim();
                if (Enum.TryParse<Rating>(rStr, true, out Rating r))
                {
                    node.Rating = r;
                }
            }
            else if (tag.StartsWith("fallback:", StringComparison.OrdinalIgnoreCase))
            {
                node.FallbackNode = tag.Substring(9).Trim();
            }
        }

        private static int IndexOfWord(string text, string word)
        {
            int idx = 0;
            while ((idx = text.IndexOf(word, idx, StringComparison.Ordinal)) >= 0)
            {
                bool startOk = idx == 0 || char.IsWhiteSpace(text[idx - 1]);
                bool endOk = (idx + word.Length == text.Length) || char.IsWhiteSpace(text[idx + word.Length]);
                if (startOk && endOk) return idx;
                idx += word.Length;
            }
            return -1;
        }

        public static string[] SplitCommandLine(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result.ToArray();

            bool inQuotes = false;
            bool tokenStarted = false;
            var sb = new StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    tokenStarted = true;
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (tokenStarted)
                    {
                        result.Add(sb.ToString());
                        sb.Clear();
                        tokenStarted = false;
                    }
                }
                else
                {
                    sb.Append(c);
                    tokenStarted = true;
                }
            }

            if (inQuotes)
            {
                throw new FormatException("Unmatched quote in command line.");
            }

            if (tokenStarted)
            {
                result.Add(sb.ToString());
            }

            return result.ToArray();
        }
    }
}
