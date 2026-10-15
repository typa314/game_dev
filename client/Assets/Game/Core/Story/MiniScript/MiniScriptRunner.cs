using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Domain;

namespace Game.Core.Story.MiniScript
{
    /// <summary>
    /// MiniScript 腳本執行器，實作 IStoryRunner 介面。
    /// </summary>
    public class MiniScriptRunner : IStoryRunner
    {
        private const int MaxNonYieldingSteps = 10000;

        private readonly MiniScript _script;
        private readonly Dictionary<string, Func<object[], object>> _functions = new Dictionary<string, Func<object[], object>>(StringComparer.Ordinal);
        private readonly Dictionary<string, Action<string[]>> _commands = new Dictionary<string, Action<string[]>>(StringComparer.Ordinal);

        private string _currentNodeName;
        private int _instructionIndex;
        private IReadOnlyList<StoryOption> _currentOptions = Array.Empty<StoryOption>();

        public string CurrentNodeName => _currentNodeName;
        public StoryLine CurrentLine { get; private set; }
        public IReadOnlyList<StoryOption> CurrentOptions => _currentOptions;
        public bool IsEnded { get; private set; }
        public Dictionary<string, object> Variables { get; } = new Dictionary<string, object>(StringComparer.Ordinal);
        IDictionary<string, object> IStoryRunner.Variables => Variables;

        public RatingPolicy RatingPolicy { get; set; }

        public MiniScriptRunner(MiniScript script, RatingPolicy ratingPolicy = null)
        {
            _script = script ?? throw new ArgumentNullException(nameof(script));
            RatingPolicy = ratingPolicy;
        }

        public MiniScriptRunner(string scriptText, RatingPolicy ratingPolicy = null)
            : this(MiniScript.Parse(scriptText), ratingPolicy)
        {
        }

        public string ResolveNodeWithFallback(string targetNode)
        {
            if (string.IsNullOrEmpty(targetNode)) return targetNode;
            if (_script.Nodes.TryGetValue(targetNode, out MiniScriptNode node))
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
            if (!_script.Nodes.ContainsKey(node))
            {
                throw new KeyNotFoundException($"Node '{node}' was not found in the script.");
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
                // Advance past current line
                _instructionIndex++;
                CurrentLine = null;
            }

            RunUntilYield();
        }

        public void Choose(int index)
        {
            if (_currentOptions == null || _currentOptions.Count == 0)
            {
                throw new InvalidOperationException("No options currently available to choose.");
            }

            if (index < 0 || index >= _currentOptions.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), $"Option index {index} is out of range [0, {_currentOptions.Count}).");
            }

            StoryOption chosen = _currentOptions[index];
            if (!chosen.IsSelectable)
            {
                throw new InvalidOperationException($"Option at index {index} ('{chosen.TextKey}') is disabled and cannot be chosen.");
            }

            string targetNode = ResolveNodeWithFallback(chosen.TargetNode);
            if (!_script.Nodes.ContainsKey(targetNode))
            {
                throw new InvalidOperationException($"Target node '{targetNode}' does not exist in script.");
            }

            _currentOptions = Array.Empty<StoryOption>();
            _currentNodeName = targetNode;
            _instructionIndex = 0;

            RunUntilYield();
        }

        public StorySnapshot Snapshot()
        {
            return new StorySnapshot(
                _currentNodeName,
                _instructionIndex,
                CurrentLine,
                _currentOptions,
                IsEnded,
                new Dictionary<string, object>(Variables, StringComparer.Ordinal));
        }

        public void Restore(StorySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            _currentNodeName = snapshot.NodeName;
            _instructionIndex = snapshot.InstructionIndex;
            CurrentLine = snapshot.CurrentLine;
            _currentOptions = snapshot.CurrentOptions != null
                ? new List<StoryOption>(snapshot.CurrentOptions)
                : (IReadOnlyList<StoryOption>)Array.Empty<StoryOption>();
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

        private void RunUntilYield()
        {
            int stepCount = 0;

            while (true)
            {
                if (IsEnded) return;

                if (string.IsNullOrEmpty(_currentNodeName) || !_script.Nodes.TryGetValue(_currentNodeName, out MiniScriptNode node))
                {
                    throw new InvalidOperationException($"Current node '{_currentNodeName}' does not exist in script.");
                }

                if (_instructionIndex >= node.Instructions.Count)
                {
                    IsEnded = true;
                    CurrentLine = null;
                    _currentOptions = Array.Empty<StoryOption>();
                    return;
                }

                stepCount++;
                if (stepCount > MaxNonYieldingSteps)
                {
                    throw new InvalidOperationException("Infinite control-flow loop detected: exceeded maximum non-yielding instruction limit.");
                }

                MiniScriptInstruction inst = node.Instructions[_instructionIndex];

                if (inst is LineInstruction lineInst)
                {
                    CurrentLine = new StoryLine(lineInst.LineId, lineInst.Speaker, lineInst.TextKey);
                    _currentOptions = Array.Empty<StoryOption>();
                    return;
                }

                if (inst is OptionInstruction)
                {
                    var visibleOptions = new List<StoryOption>();
                    int optIdx = _instructionIndex;

                    while (optIdx < node.Instructions.Count && node.Instructions[optIdx] is OptionInstruction optInst)
                    {
                        bool isVisible = true;
                        if (!string.IsNullOrEmpty(optInst.Condition))
                        {
                            isVisible = MiniScriptConditionEvaluator.Evaluate(optInst.Condition, Variables, _functions);
                        }

                        if (isVisible)
                        {
                            visibleOptions.Add(new StoryOption(optInst.TextKey, optInst.TargetNode, optInst.IsSelectable));
                        }

                        optIdx++;
                    }

                    _instructionIndex = optIdx;
                    CurrentLine = null;
                    _currentOptions = visibleOptions.AsReadOnly();
                    if (_currentOptions.Count > 0)
                        return;
                    continue;
                }

                if (inst is SetInstruction setInst)
                {
                    object val = MiniScriptExpressionEvaluator.EvaluateSetExpression(setInst.Expression, Variables, _functions);
                    Variables[setInst.VariableName] = val;
                    _instructionIndex++;
                    continue;
                }

                if (inst is IfInstruction ifInst)
                {
                    bool conditionPassed = MiniScriptConditionEvaluator.Evaluate(ifInst.Condition, Variables, _functions);
                    if (conditionPassed)
                    {
                        string targetNode = ResolveNodeWithFallback(ifInst.TargetNode);
                        if (!_script.Nodes.ContainsKey(targetNode))
                        {
                            throw new InvalidOperationException($"Target node '{targetNode}' does not exist in script.");
                        }

                        _currentNodeName = targetNode;
                        _instructionIndex = 0;
                    }
                    else
                    {
                        _instructionIndex++;
                    }
                    continue;
                }

                if (inst is JumpInstruction jumpInst)
                {
                    string targetNode = ResolveNodeWithFallback(jumpInst.TargetNode);
                    if (!_script.Nodes.ContainsKey(targetNode))
                    {
                        throw new InvalidOperationException($"Target node '{targetNode}' does not exist in script.");
                    }

                    _currentNodeName = targetNode;
                    _instructionIndex = 0;
                    continue;
                }

                if (inst is CommandInstruction cmdInst)
                {
                    if (!_commands.TryGetValue(cmdInst.CommandName, out Action<string[]> handler))
                    {
                        throw new InvalidOperationException($"Line {cmdInst.LineNumber}: unknown story command '{cmdInst.CommandName}'.");
                    }

                    handler(cmdInst.Arguments);
                    _instructionIndex++;
                    continue;
                }

                if (inst is EndInstruction)
                {
                    IsEnded = true;
                    CurrentLine = null;
                    _currentOptions = Array.Empty<StoryOption>();
                    return;
                }

                throw new InvalidOperationException($"Unhandled instruction type '{inst.GetType().Name}'.");
            }
        }
    }

    /// <summary>
    /// 條件表達式評估器，支援比較運算子、布林值、數值、字串與注入之遊戲狀態函式呼叫。
    /// </summary>
    public static class MiniScriptConditionEvaluator
    {
        public static bool Evaluate(
            string condition,
            IDictionary<string, object> variables,
            IDictionary<string, Func<object[], object>> functions)
        {
            if (string.IsNullOrWhiteSpace(condition))
            {
                throw new FormatException("Empty condition expression.");
            }

            condition = StripOuterParentheses(condition.Trim());

            if (string.Equals(condition, "true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(condition, "false", StringComparison.OrdinalIgnoreCase))
                return false;

            if (TryFindComparisonOperator(condition, out int opPos, out string op))
            {
                string leftStr = condition.Substring(0, opPos).Trim();
                string rightStr = condition.Substring(opPos + op.Length).Trim();

                if (string.IsNullOrEmpty(leftStr) || string.IsNullOrEmpty(rightStr))
                {
                    throw new FormatException($"Malformed condition: missing operand in '{condition}'.");
                }

                object leftVal = ResolveOperand(leftStr, variables, functions);
                object rightVal = ResolveOperand(rightStr, variables, functions);

                return CompareValues(leftVal, op, rightVal);
            }

            if (condition.StartsWith("!"))
            {
                return !Evaluate(condition.Substring(1).Trim(), variables, functions);
            }

            object val = ResolveOperand(condition, variables, functions);
            return ToBoolean(val);
        }

        public static string StripOuterParentheses(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            while (text.StartsWith("(") && text.EndsWith(")"))
            {
                int depth = 0;
                bool isEnclosing = true;
                for (int i = 0; i < text.Length - 1; i++)
                {
                    if (text[i] == '(') depth++;
                    else if (text[i] == ')') depth--;
                    if (depth == 0)
                    {
                        isEnclosing = false;
                        break;
                    }
                }
                if (isEnclosing)
                {
                    text = text.Substring(1, text.Length - 2).Trim();
                }
                else
                {
                    break;
                }
            }
            return text;
        }

        private static bool TryFindComparisonOperator(string text, out int opPos, out string op)
        {
            opPos = -1;
            op = null;

            int parenDepth = 0;
            bool inQuotes = false;

            // Two-char operators: ==, !=, <=, >=
            for (int i = 0; i < text.Length - 1; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (inQuotes) continue;

                if (c == '(') parenDepth++;
                else if (c == ')') parenDepth--;
                else if (parenDepth == 0)
                {
                    string candidate = text.Substring(i, 2);
                    if (candidate == "==" || candidate == "!=" || candidate == "<=" || candidate == ">=")
                    {
                        opPos = i;
                        op = candidate;
                        return true;
                    }
                }
            }

            // Single-char operators: <, > (excluding ->)
            parenDepth = 0;
            inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (inQuotes) continue;

                if (c == '(') parenDepth++;
                else if (c == ')') parenDepth--;
                else if (parenDepth == 0)
                {
                    if (c == '<' || c == '>')
                    {
                        if (c == '>' && i > 0 && text[i - 1] == '-')
                            continue;
                        if (c == '<' && i + 1 < text.Length && text[i + 1] == '-')
                            continue;

                        opPos = i;
                        op = c.ToString();
                        return true;
                    }
                }
            }

            return false;
        }

        private static object ResolveOperand(
            string expr,
            IDictionary<string, object> variables,
            IDictionary<string, Func<object[], object>> functions)
        {
            expr = expr.Trim();
            if (string.IsNullOrEmpty(expr))
            {
                throw new FormatException("Empty operand in condition.");
            }

            // Quoted string
            if (expr.Length >= 2 && expr.StartsWith("\"") && expr.EndsWith("\""))
            {
                return expr.Substring(1, expr.Length - 2);
            }

            // Boolean literal
            if (string.Equals(expr, "true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(expr, "false", StringComparison.OrdinalIgnoreCase))
                return false;

            // Function call: name(...)
            int openParen = expr.IndexOf('(');
            if (openParen > 0 && expr.EndsWith(")"))
            {
                string funcName = expr.Substring(0, openParen).Trim();
                string argsContent = expr.Substring(openParen + 1, expr.Length - openParen - 2).Trim();

                object[] args = ParseFunctionArguments(argsContent, variables, functions);

                if (!functions.TryGetValue(funcName, out Func<object[], object> fn))
                {
                    throw new InvalidOperationException($"Unknown game-state function '{funcName}'.");
                }

                return fn(args);
            }

            // Numeric literals
            if (int.TryParse(expr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iVal))
                return iVal;
            if (long.TryParse(expr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lVal))
                return lVal;
            if (double.TryParse(expr, NumberStyles.Float, CultureInfo.InvariantCulture, out double dVal))
                return dVal;

            // Variable lookup
            if (variables.TryGetValue(expr, out object varVal))
                return varVal;

            // Numeric arithmetic expression
            if (expr.Contains("+") || expr.Contains("-"))
            {
                return MiniScriptExpressionEvaluator.EvaluateSetExpression(expr, variables, functions);
            }

            throw new InvalidOperationException($"Cannot resolve condition operand or variable '{expr}'.");
        }

        internal static object[] ParseFunctionArguments(
            string argsContent,
            IDictionary<string, object> variables,
            IDictionary<string, Func<object[], object>> functions)
        {
            if (string.IsNullOrWhiteSpace(argsContent))
                return Array.Empty<object>();

            var rawArgs = new List<string>();
            int depth = 0;
            bool inQuotes = false;
            int start = 0;

            for (int i = 0; i < argsContent.Length; i++)
            {
                char c = argsContent[i];
                if (c == '"') inQuotes = !inQuotes;
                else if (!inQuotes)
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                    else if (c == ',' && depth == 0)
                    {
                        rawArgs.Add(argsContent.Substring(start, i - start).Trim());
                        start = i + 1;
                    }
                }
            }

            if (start < argsContent.Length)
            {
                rawArgs.Add(argsContent.Substring(start).Trim());
            }

            var result = new object[rawArgs.Count];
            for (int i = 0; i < rawArgs.Count; i++)
            {
                result[i] = ResolveOperand(rawArgs[i], variables, functions);
            }
            return result;
        }

        private static bool ToBoolean(object val)
        {
            if (val == null) return false;
            if (val is bool b) return b;
            if (IsNumeric(val))
            {
                double d = Convert.ToDouble(val, CultureInfo.InvariantCulture);
                return Math.Abs(d) > 1e-9;
            }
            if (val is string s)
            {
                if (bool.TryParse(s, out bool sb)) return sb;
                return !string.IsNullOrEmpty(s);
            }
            return true;
        }

        private static bool CompareValues(object left, string op, object right)
        {
            if (IsExactNumeric(left) && IsExactNumeric(right))
            {
                decimal l = Convert.ToDecimal(left, CultureInfo.InvariantCulture);
                decimal r = Convert.ToDecimal(right, CultureInfo.InvariantCulture);

                return op switch
                {
                    "==" => l == r,
                    "!=" => l != r,
                    "<"  => l < r,
                    "<=" => l <= r,
                    ">"  => l > r,
                    ">=" => l >= r,
                    _ => throw new InvalidOperationException($"Unsupported operator '{op}'.")
                };
            }

            if (IsNumeric(left) && IsNumeric(right))
            {
                double l = Convert.ToDouble(left, CultureInfo.InvariantCulture);
                double r = Convert.ToDouble(right, CultureInfo.InvariantCulture);

                return op switch
                {
                    "==" => Math.Abs(l - r) < 1e-9,
                    "!=" => Math.Abs(l - r) >= 1e-9,
                    "<"  => l < r,
                    "<=" => l <= r,
                    ">"  => l > r,
                    ">=" => l >= r,
                    _ => throw new InvalidOperationException($"Unsupported operator '{op}'.")
                };
            }

            if (left is bool lb && right is bool rb)
            {
                return op switch
                {
                    "==" => lb == rb,
                    "!=" => lb != rb,
                    _ => throw new InvalidOperationException($"Unsupported operator '{op}' for booleans.")
                };
            }

            string ls = left?.ToString();
            string rs = right?.ToString();

            return op switch
            {
                "==" => string.Equals(ls, rs, StringComparison.Ordinal),
                "!=" => !string.Equals(ls, rs, StringComparison.Ordinal),
                "<"  => string.CompareOrdinal(ls, rs) < 0,
                "<=" => string.CompareOrdinal(ls, rs) <= 0,
                ">"  => string.CompareOrdinal(ls, rs) > 0,
                ">=" => string.CompareOrdinal(ls, rs) >= 0,
                _ => throw new InvalidOperationException($"Unsupported operator '{op}'.")
            };
        }

        private static bool IsExactNumeric(object o)
        {
            return o is sbyte or byte or short or ushort or int or uint or long or ulong or decimal;
        }

        public static bool IsNumeric(object o)
        {
            return o is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
        }
    }

    /// <summary>
    /// 算術與賦值表達式評估器，支援常數、字串、布林值與依賴變數之 + / - 運算。
    /// </summary>
    public static class MiniScriptExpressionEvaluator
    {
        public static object EvaluateSetExpression(
            string expr,
            IDictionary<string, object> variables,
            IDictionary<string, Func<object[], object>> functions = null)
        {
            expr = expr.Trim();
            if (string.IsNullOrEmpty(expr))
            {
                throw new InvalidOperationException("Empty expression in set instruction.");
            }

            // Quoted string
            if (expr.Length >= 2 && expr.StartsWith("\"") && expr.EndsWith("\""))
            {
                return expr.Substring(1, expr.Length - 2);
            }

            // Boolean literal
            if (string.Equals(expr, "true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(expr, "false", StringComparison.OrdinalIgnoreCase))
                return false;

            // Single variable reference
            if (variables.TryGetValue(expr, out object varVal))
            {
                return varVal;
            }

            // Numeric evaluation
            return EvaluateNumericExpression(expr, variables, functions);
        }

        public static object EvaluateNumericExpression(
            string expr,
            IDictionary<string, object> variables,
            IDictionary<string, Func<object[], object>> functions = null)
        {
            var terms = new List<string>();
            var ops = new List<char>();

            int i = 0;
            int len = expr.Length;
            int termStart = 0;
            bool expectUnary = true;
            int parenDepth = 0;

            while (i < len)
            {
                char c = expr[i];
                if (c == '(') parenDepth++;
                else if (c == ')') parenDepth--;
                else if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if ((c == '+' || c == '-') && !expectUnary && parenDepth == 0)
                {
                    string term = expr.Substring(termStart, i - termStart).Trim();
                    if (string.IsNullOrEmpty(term))
                    {
                        throw new InvalidOperationException($"Malformed arithmetic expression: '{expr}'.");
                    }
                    terms.Add(term);
                    ops.Add(c);
                    i++;
                    termStart = i;
                    expectUnary = true;
                    continue;
                }

                expectUnary = false;
                i++;
            }

            if (termStart < len)
            {
                string lastTerm = expr.Substring(termStart).Trim();
                if (!string.IsNullOrEmpty(lastTerm))
                {
                    terms.Add(lastTerm);
                }
            }

            if (terms.Count == 0 || terms.Count != ops.Count + 1)
            {
                throw new InvalidOperationException($"Malformed arithmetic expression: '{expr}'.");
            }

            double current = ResolveNumber(terms[0], variables, functions, out bool isIntResult);

            for (int j = 0; j < ops.Count; j++)
            {
                double next = ResolveNumber(terms[j + 1], variables, functions, out bool nextIsInt);
                if (!nextIsInt) isIntResult = false;

                if (ops[j] == '+') current += next;
                else if (ops[j] == '-') current -= next;
            }

            if (isIntResult && current >= int.MinValue && current <= int.MaxValue && Math.Abs(current - Math.Round(current)) < 1e-9)
            {
                return (int)Math.Round(current);
            }
            if (isIntResult && current >= long.MinValue && current <= long.MaxValue && Math.Abs(current - Math.Round(current)) < 1e-9)
            {
                return (long)Math.Round(current);
            }

            return current;
        }

        private static double ResolveNumber(
            string term,
            IDictionary<string, object> variables,
            IDictionary<string, Func<object[], object>> functions,
            out bool isInt)
        {
            term = term.Trim();

            if (int.TryParse(term, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iVal))
            {
                isInt = true;
                return iVal;
            }
            if (long.TryParse(term, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lVal))
            {
                isInt = true;
                return lVal;
            }
            if (double.TryParse(term, NumberStyles.Float, CultureInfo.InvariantCulture, out double dVal))
            {
                isInt = false;
                return dVal;
            }

            if (variables.TryGetValue(term, out object v))
            {
                if (v is int or long or short or byte)
                {
                    isInt = true;
                    return Convert.ToDouble(v, CultureInfo.InvariantCulture);
                }
                if (v is float or double or decimal)
                {
                    isInt = false;
                    return Convert.ToDouble(v, CultureInfo.InvariantCulture);
                }
                if (double.TryParse(v?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                {
                    string str = v?.ToString();
                    isInt = str != null && !str.Contains(".");
                    return parsed;
                }
                throw new InvalidOperationException($"Variable '{term}' with value '{v}' is not numeric.");
            }

            // Function call: name(...)
            int openParen = term.IndexOf('(');
            if (openParen > 0 && term.EndsWith(")") && functions != null)
            {
                string funcName = term.Substring(0, openParen).Trim();
                string argsContent = term.Substring(openParen + 1, term.Length - openParen - 2).Trim();
                object[] args = MiniScriptConditionEvaluator.ParseFunctionArguments(argsContent, variables, functions);
                if (!functions.TryGetValue(funcName, out Func<object[], object> fn))
                {
                    throw new InvalidOperationException($"Unknown game-state function '{funcName}'.");
                }
                object fnResult = fn(args);
                if (fnResult is int or long or short or byte)
                {
                    isInt = true;
                    return Convert.ToDouble(fnResult, CultureInfo.InvariantCulture);
                }
                if (fnResult is float or double or decimal)
                {
                    isInt = false;
                    return Convert.ToDouble(fnResult, CultureInfo.InvariantCulture);
                }
                if (double.TryParse(fnResult?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedFn))
                {
                    string str = fnResult?.ToString();
                    isInt = str != null && !str.Contains(".");
                    return parsedFn;
                }
                throw new InvalidOperationException($"Function '{funcName}' returned non-numeric value '{fnResult}'.");
            }

            throw new InvalidOperationException($"Cannot resolve numeric value from '{term}'.");
        }
    }
}
