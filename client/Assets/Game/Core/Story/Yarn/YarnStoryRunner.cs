using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Game.Domain;
using Game.Core.Localization;

namespace Game.Core.Story.Yarn
{
    public sealed class YarnNode
    {
        public string Title { get; set; }
        public Rating Rating { get; set; } = Rating.G;
        public string FallbackNode { get; set; }
        public List<string> Tags { get; } = new List<string>();
        public List<YarnInstruction> Instructions { get; } = new List<YarnInstruction>();
    }

    public enum YarnInstructionType
    {
        Line,
        Command,
        Option,
        Jump,
        IfBranch,
        Set
    }

    public sealed class YarnInstruction
    {
        public YarnInstructionType Type { get; }
        public int LineNumber { get; }
        public string Speaker { get; set; }
        public string Text { get; set; }
        public string LineId { get; set; }
        public string CommandName { get; set; }
        public string[] Arguments { get; set; }
        public string TargetNode { get; set; }
        public string ConditionExpr { get; set; }
        public List<YarnInstruction> SubInstructions { get; } = new List<YarnInstruction>();

        public YarnInstruction(YarnInstructionType type, int lineNumber)
        {
            Type = type;
            LineNumber = lineNumber;
        }
    }

    /// <summary>
    /// Yarn Spinner 格式解析器與執行器，實作 IStoryRunner 介面 (工單 F6)
    /// 支援 Yarn 標準節點、#line: ID 國際化綁定、分支跳轉、選項群組、條件判斷與快照回溯。
    /// </summary>
    public class YarnStoryRunner : IStoryRunner
    {
        private const int MaxNonYieldingSteps = 10000;

        private readonly Dictionary<string, YarnNode> _nodes = new Dictionary<string, YarnNode>(StringComparer.Ordinal);
        private readonly Dictionary<string, Func<object[], object>> _functions = new Dictionary<string, Func<object[], object>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Action<string[]>> _commands = new Dictionary<string, Action<string[]>>(StringComparer.Ordinal);

        private string _currentNodeName;
        private int _instructionIndex;
        private IReadOnlyList<StoryOption> _currentOptions = Array.Empty<StoryOption>();
        private readonly ILocalizationService _loc;

        public string CurrentNodeName => _currentNodeName;
        public StoryLine CurrentLine { get; private set; }
        public IReadOnlyList<StoryOption> CurrentOptions => _currentOptions;
        public bool IsEnded { get; private set; }
        public Dictionary<string, object> Variables { get; } = new Dictionary<string, object>(StringComparer.Ordinal);
        IDictionary<string, object> IStoryRunner.Variables => Variables;

        public RatingPolicy RatingPolicy { get; set; }
        public IReadOnlyDictionary<string, YarnNode> Nodes => _nodes;

        public YarnStoryRunner(string yarnSource, RatingPolicy ratingPolicy = null, ILocalizationService loc = null)
        {
            RatingPolicy = ratingPolicy;
            _loc = loc;
            ParseYarn(yarnSource);
        }

        public void RegisterFunction(string name, Func<object[], object> function)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            _functions[name] = function ?? throw new ArgumentNullException(nameof(function));
        }

        public void RegisterCommand(string name, Action<string[]> handler)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            _commands[name] = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public void Start(string node)
        {
            if (string.IsNullOrEmpty(node)) throw new ArgumentNullException(nameof(node));

            node = ResolveNodeWithFallback(node);
            if (!_nodes.ContainsKey(node))
            {
                throw new KeyNotFoundException($"Node '{node}' was not found in Yarn script.");
            }

            _currentNodeName = node;
            _instructionIndex = 0;
            CurrentLine = null;
            _currentOptions = Array.Empty<StoryOption>();
            IsEnded = false;
            Variables.Clear();
        }

        public void Continue()
        {
            if (IsEnded) return;

            if (_currentOptions != null && _currentOptions.Count > 0)
            {
                throw new InvalidOperationException("Cannot Continue() while an option group is visible. Call Choose(index) instead.");
            }

            if (CurrentLine != null)
            {
                _instructionIndex++;
                CurrentLine = null;
            }

            ExecuteUntilYield();
        }

        public void Choose(int index)
        {
            if (IsEnded) throw new InvalidOperationException("Story has already ended.");
            if (_currentOptions == null || _currentOptions.Count == 0)
                throw new InvalidOperationException("No options currently available to choose from.");

            if (index < 0 || index >= _currentOptions.Count)
                throw new ArgumentOutOfRangeException(nameof(index), $"Invalid choice index {index}. Total options: {_currentOptions.Count}");

            var chosen = _currentOptions[index];
            if (!chosen.IsSelectable)
                throw new InvalidOperationException($"Option '{chosen.TextKey}' is disabled and cannot be selected.");

            _currentOptions = Array.Empty<StoryOption>();

            if (!string.IsNullOrEmpty(chosen.TargetNode))
            {
                JumpToNode(chosen.TargetNode);
                ExecuteUntilYield();
            }
            else
            {
                _instructionIndex++;
                ExecuteUntilYield();
            }
        }

        public StorySnapshot Snapshot()
        {
            return new StorySnapshot(
                _currentNodeName,
                _instructionIndex,
                CurrentLine,
                _currentOptions,
                IsEnded,
                Variables);
        }

        public void Restore(StorySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            _currentNodeName = snapshot.NodeName;
            _instructionIndex = snapshot.InstructionIndex;
            CurrentLine = snapshot.CurrentLine;
            _currentOptions = snapshot.CurrentOptions ?? Array.Empty<StoryOption>();
            IsEnded = snapshot.IsEnded;

            Variables.Clear();
            if (snapshot.Variables != null)
            {
                foreach (var kvp in snapshot.Variables)
                {
                    Variables[kvp.Key] = kvp.Value;
                }
            }
        }

        public string ResolveNodeWithFallback(string targetNode)
        {
            if (string.IsNullOrEmpty(targetNode)) return targetNode;
            if (_nodes.TryGetValue(targetNode, out YarnNode node))
            {
                if (RatingPolicy != null && !RatingPolicy.Allows(node.Rating))
                {
                    if (!string.IsNullOrEmpty(node.FallbackNode))
                    {
                        return ResolveNodeWithFallback(node.FallbackNode);
                    }
                }
            }
            return targetNode;
        }

        private void JumpToNode(string targetNode)
        {
            string resolved = ResolveNodeWithFallback(targetNode);
            if (!_nodes.ContainsKey(resolved))
            {
                throw new KeyNotFoundException($"Jump target node '{resolved}' was not found.");
            }
            _currentNodeName = resolved;
            _instructionIndex = 0;
            CurrentLine = null;
            _currentOptions = Array.Empty<StoryOption>();
        }

        private void ExecuteUntilYield()
        {
            int safetyCount = 0;

            while (!IsEnded && CurrentLine == null && (_currentOptions == null || _currentOptions.Count == 0))
            {
                if (++safetyCount > MaxNonYieldingSteps)
                    throw new InvalidOperationException($"Infinite loop detected in node '{_currentNodeName}' at instruction {_instructionIndex}.");

                if (string.IsNullOrEmpty(_currentNodeName) || !_nodes.TryGetValue(_currentNodeName, out YarnNode node))
                {
                    IsEnded = true;
                    return;
                }

                if (_instructionIndex >= node.Instructions.Count)
                {
                    IsEnded = true;
                    return;
                }

                var inst = node.Instructions[_instructionIndex];

                switch (inst.Type)
                {
                    case YarnInstructionType.Line:
                    {
                        string lineId = inst.LineId ?? string.Empty;
                        string textKey = inst.Text ?? string.Empty;
                        
                        // 若有注入在地化服務，且有對應 lineId，可解析多語言字串
                        if (_loc != null && !string.IsNullOrEmpty(lineId))
                        {
                            string locText = _loc.Get($"loc:story/{lineId}");
                            if (locText.StartsWith("[") && locText.EndsWith("]"))
                            {
                                locText = _loc.Get(lineId);
                            }
                            if (!locText.StartsWith("[") || !locText.EndsWith("]"))
                            {
                                textKey = locText;
                            }
                        }

                        CurrentLine = new StoryLine(lineId, inst.Speaker, textKey);
                        return; // Yield to caller
                    }

                    case YarnInstructionType.Option:
                    {
                        // 收集連續的選項指令組成 option group
                        var options = new List<StoryOption>();
                        int optScan = _instructionIndex;

                        while (optScan < node.Instructions.Count && node.Instructions[optScan].Type == YarnInstructionType.Option)
                        {
                            var optInst = node.Instructions[optScan];
                            bool selectable = true;
                            if (!string.IsNullOrWhiteSpace(optInst.ConditionExpr))
                            {
                                selectable = EvaluateCondition(optInst.ConditionExpr);
                            }

                            string optText = optInst.Text;
                            if (_loc != null && !string.IsNullOrEmpty(optInst.LineId))
                            {
                                string locOpt = _loc.Get($"loc:story/{optInst.LineId}");
                                if (!locOpt.StartsWith("[") || !locOpt.EndsWith("]"))
                                    optText = locOpt;
                            }

                            options.Add(new StoryOption(optText, optInst.TargetNode, selectable));
                            optScan++;
                        }

                        _currentOptions = options;
                        _instructionIndex = optScan - 1; // 選擇後再 +1
                        return; // Yield to caller
                    }

                    case YarnInstructionType.Command:
                    {
                        ExecuteCommand(inst);
                        _instructionIndex++;
                        break;
                    }

                    case YarnInstructionType.Set:
                    {
                        ExecuteSet(inst.ConditionExpr);
                        _instructionIndex++;
                        break;
                    }

                    case YarnInstructionType.Jump:
                    {
                        JumpToNode(inst.TargetNode);
                        break;
                    }

                    case YarnInstructionType.IfBranch:
                    {
                        ExecuteIfBranch(inst);
                        break;
                    }
                }
            }
        }

        private void ExecuteCommand(YarnInstruction inst)
        {
            if (string.IsNullOrEmpty(inst.CommandName)) return;

            if (_commands.TryGetValue(inst.CommandName, out var handler))
            {
                handler(inst.Arguments ?? Array.Empty<string>());
            }
        }

        private void ExecuteSet(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return;

            // 支援: <<set $var = expr>> 或 <<set $var to expr>>
            string trimmed = expr.Trim();
            int eqIdx = trimmed.IndexOf('=');
            int toIdx = -1;

            if (eqIdx < 0)
            {
                var match = Regex.Match(trimmed, @"\s+to\s+", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    toIdx = match.Index;
                    eqIdx = toIdx;
                }
            }

            if (eqIdx < 0)
            {
                throw new FormatException($"Invalid set expression in Yarn: '{expr}'. Expected '$var = value' or '$var to value'.");
            }

            string varName = trimmed.Substring(0, eqIdx).Trim().TrimStart('$');
            string rightExpr = toIdx >= 0 ? trimmed.Substring(toIdx + 4).Trim() : trimmed.Substring(eqIdx + 1).Trim();

            object val = EvaluateExpression(rightExpr);
            Variables[varName] = val;
        }

        private void ExecuteIfBranch(YarnInstruction inst)
        {
            // 條件分支
            bool cond = EvaluateCondition(inst.ConditionExpr);
            if (cond)
            {
                if (!string.IsNullOrEmpty(inst.TargetNode))
                {
                    JumpToNode(inst.TargetNode);
                }
                else
                {
                    _instructionIndex++;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(inst.Text)) // Text 暫存 Else/TargetNode
                {
                    JumpToNode(inst.Text);
                }
                else
                {
                    _instructionIndex++;
                }
            }
        }

        private bool EvaluateCondition(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return true;
            string trimmed = expr.Trim();

            // 簡易比較判斷 (==, !=, >=, <=, >, <)
            string[] ops = new[] { "==", "!=", ">=", "<=", ">", "<" };
            foreach (var op in ops)
            {
                int idx = trimmed.IndexOf(op, StringComparison.Ordinal);
                if (idx > 0)
                {
                    string leftStr = trimmed.Substring(0, idx).Trim();
                    string rightStr = trimmed.Substring(idx + op.Length).Trim();

                    double left = Convert.ToDouble(EvaluateExpression(leftStr), CultureInfo.InvariantCulture);
                    double right = Convert.ToDouble(EvaluateExpression(rightStr), CultureInfo.InvariantCulture);

                    switch (op)
                    {
                        case "==": return Math.Abs(left - right) < 1e-6;
                        case "!=": return Math.Abs(left - right) >= 1e-6;
                        case ">=": return left >= right - 1e-6;
                        case "<=": return left <= right + 1e-6;
                        case ">": return left > right;
                        case "<": return left < right;
                    }
                }
            }

            // 單元布林判斷
            object singleVal = EvaluateExpression(trimmed);
            if (singleVal is bool b) return b;
            if (singleVal is double d) return Math.Abs(d) > 1e-6;
            return singleVal != null;
        }

        private object EvaluateExpression(string expr)
        {
            string trimmed = expr.Trim();

            // 算術加減運算 (例如 $var + 1 或 $var - 1)
            int plusIdx = trimmed.IndexOf('+');
            if (plusIdx > 0)
            {
                double l = Convert.ToDouble(EvaluateExpression(trimmed.Substring(0, plusIdx)), CultureInfo.InvariantCulture);
                double r = Convert.ToDouble(EvaluateExpression(trimmed.Substring(plusIdx + 1)), CultureInfo.InvariantCulture);
                return l + r;
            }

            int minusIdx = trimmed.IndexOf('-');
            if (minusIdx > 0)
            {
                double l = Convert.ToDouble(EvaluateExpression(trimmed.Substring(0, minusIdx)), CultureInfo.InvariantCulture);
                double r = Convert.ToDouble(EvaluateExpression(trimmed.Substring(minusIdx + 1)), CultureInfo.InvariantCulture);
                return l - r;
            }

            // 函式呼叫例如 affection("char01_claire")
            var fnMatch = Regex.Match(trimmed, @"^([a-zA-Z0-9_]+)\((.*)\)$");
            if (fnMatch.Success)
            {
                string fnName = fnMatch.Groups[1].Value;
                string argStr = fnMatch.Groups[2].Value.Trim().Trim('"');
                if (_functions.TryGetValue(fnName, out var fn))
                {
                    return fn(new object[] { argStr });
                }
            }

            // 變數參照
            if (trimmed.StartsWith("$"))
            {
                string vName = trimmed.TrimStart('$');
                if (Variables.TryGetValue(vName, out object v)) return v;
                return 0.0;
            }

            // 數值
            if (double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out double num))
                return num;

            // 字串
            if (trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
                return trimmed.Substring(1, trimmed.Length - 2);

            if (bool.TryParse(trimmed, out bool bVal))
                return bVal;

            return trimmed;
        }

        private void ParseYarn(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return;

            using (var reader = new StringReader(source))
            {
                string line;
                YarnNode currentNode = null;
                bool inBody = false;
                int lineNum = 0;

                while ((line = reader.ReadLine()) != null)
                {
                    lineNum++;
                    string trimmed = line.Trim();

                    if (!inBody)
                    {
                        if (trimmed.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                        {
                            currentNode = new YarnNode
                            {
                                Title = trimmed.Substring(6).Trim()
                            };
                        }
                        else if (trimmed.StartsWith("tags:", StringComparison.OrdinalIgnoreCase) && currentNode != null)
                        {
                            string tagLine = trimmed.Substring(5).Trim();
                            string[] tags = tagLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var tag in tags)
                            {
                                currentNode.Tags.Add(tag);
                                if (tag.StartsWith("rating:", StringComparison.OrdinalIgnoreCase))
                                {
                                    string rStr = tag.Substring(7).Trim();
                                    if (Enum.TryParse<Rating>(rStr, true, out var r))
                                        currentNode.Rating = r;
                                }
                                else if (tag.StartsWith("fallback:", StringComparison.OrdinalIgnoreCase))
                                {
                                    currentNode.FallbackNode = tag.Substring(9).Trim();
                                }
                            }
                        }
                        else if (trimmed == "---" && currentNode != null)
                        {
                            inBody = true;
                        }
                    }
                    else
                    {
                        if (trimmed == "===")
                        {
                            if (currentNode != null && !string.IsNullOrEmpty(currentNode.Title))
                            {
                                _nodes[currentNode.Title] = currentNode;
                            }
                            currentNode = null;
                            inBody = false;
                            continue;
                        }

                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("//"))
                            continue;

                        // 解析 Body 內指令
                        ParseBodyLine(currentNode, trimmed, lineNum);
                    }
                }

                if (currentNode != null && !string.IsNullOrEmpty(currentNode.Title))
                {
                    _nodes[currentNode.Title] = currentNode;
                }
            }
        }

        private void ParseBodyLine(YarnNode node, string line, int lineNum)
        {
            // 提取 #line: ID (相容行尾或行中位置)
            string lineId = null;
            string content = line;
            var lineMatch = Regex.Match(content, @"#line:([a-zA-Z0-9_]+)");
            if (lineMatch.Success)
            {
                lineId = lineMatch.Groups[1].Value;
                content = content.Remove(lineMatch.Index, lineMatch.Length).Trim();
            }

            // 1. 選項: -> Text #line:xxx <<jump Node>> 或 -> Text -> Node
            if (content.StartsWith("->"))
            {
                string optBody = content.Substring(2).Trim();
                string targetNode = null;
                string condExpr = null;

                // 檢查 <<jump Target>>
                var jumpMatch = Regex.Match(optBody, @"<<jump\s+([a-zA-Z0-9_]+)>>", RegexOptions.IgnoreCase);
                if (jumpMatch.Success)
                {
                    targetNode = jumpMatch.Groups[1].Value;
                    optBody = optBody.Substring(0, jumpMatch.Index).Trim();
                }

                // 檢查 -> Target
                int arrowIdx = optBody.LastIndexOf("->", StringComparison.Ordinal);
                if (arrowIdx > 0 && targetNode == null)
                {
                    targetNode = optBody.Substring(arrowIdx + 2).Trim();
                    optBody = optBody.Substring(0, arrowIdx).Trim();
                }

                var optInst = new YarnInstruction(YarnInstructionType.Option, lineNum)
                {
                    Text = optBody.Trim('"', ' '),
                    LineId = lineId,
                    TargetNode = targetNode,
                    ConditionExpr = condExpr
                };
                node.Instructions.Add(optInst);
                return;
            }

            // 2. 指令: <<command args>>
            if (content.StartsWith("<<") && content.EndsWith(">>"))
            {
                string cmdBody = content.Substring(2, content.Length - 4).Trim();

                if (cmdBody.StartsWith("jump ", StringComparison.OrdinalIgnoreCase))
                {
                    string target = cmdBody.Substring(5).Trim();
                    node.Instructions.Add(new YarnInstruction(YarnInstructionType.Jump, lineNum)
                    {
                        TargetNode = target
                    });
                    return;
                }

                if (cmdBody.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
                {
                    string setExpr = cmdBody.Substring(4).Trim();
                    node.Instructions.Add(new YarnInstruction(YarnInstructionType.Set, lineNum)
                    {
                        ConditionExpr = setExpr
                    });
                    return;
                }

                if (cmdBody.StartsWith("if ", StringComparison.OrdinalIgnoreCase))
                {
                    // <<if condition>> <<jump Target>> 或 <<if condition -> Target>>
                    string ifBody = cmdBody.Substring(3).Trim();
                    string target = null;
                    var jm = Regex.Match(ifBody, @"jump\s+([a-zA-Z0-9_]+)", RegexOptions.IgnoreCase);
                    if (jm.Success)
                    {
                        target = jm.Groups[1].Value;
                        ifBody = ifBody.Substring(0, jm.Index).Trim();
                    }
                    node.Instructions.Add(new YarnInstruction(YarnInstructionType.IfBranch, lineNum)
                    {
                        ConditionExpr = ifBody,
                        TargetNode = target
                    });
                    return;
                }

                // 自訂遊戲演出與狀態指令 (<<bg ...>>, <<show ...>>, <<cg ...>>, <<expr ...>>)
                string[] parts = cmdBody.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    string cmdName = parts[0];
                    string[] args = new string[parts.Length - 1];
                    Array.Copy(parts, 1, args, 0, args.Length);

                    node.Instructions.Add(new YarnInstruction(YarnInstructionType.Command, lineNum)
                    {
                        CommandName = cmdName,
                        Arguments = args
                    });
                }
                return;
            }

            // 3. 對話行: Speaker: Text
            int colonIdx = content.IndexOf(':');
            string speaker = string.Empty;
            string text = content;
            if (colonIdx > 0)
            {
                speaker = content.Substring(0, colonIdx).Trim();
                text = content.Substring(colonIdx + 1).Trim();
            }

            var lineInst = new YarnInstruction(YarnInstructionType.Line, lineNum)
            {
                Speaker = speaker,
                Text = text,
                LineId = lineId
            };
            node.Instructions.Add(lineInst);
        }
    }
}
