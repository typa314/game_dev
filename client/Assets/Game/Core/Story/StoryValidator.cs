using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Story.MiniScript;
using Game.Domain;
using Game.Domain.MasterData;

namespace Game.Core.Story
{
    public sealed class StoryValidationError
    {
        public const string TargetNodeNotFound = "ERR_TARGET_NODE_NOT_FOUND";
        public const string MissingLineId = "ERR_MISSING_LINE_ID";
        public const string DuplicateLineId = "ERR_DUPLICATE_LINE_ID";
        public const string InvalidCharacterId = "ERR_INVALID_CHARACTER_ID";
        public const string InvalidSceneId = "ERR_INVALID_SCENE_ID";
        public const string InvalidCgId = "ERR_INVALID_CG_ID";
        public const string InvalidEndingId = "ERR_INVALID_ENDING_ID";
        public const string R18MissingFallback = "ERR_R18_MISSING_FALLBACK";
        public const string FallbackNodeNotFound = "ERR_FALLBACK_NODE_NOT_FOUND";
        public const string UnreachableEnding = "ERR_UNREACHABLE_ENDING";
        public const string UnregisteredCommand = "ERR_UNREGISTERED_COMMAND";

        public string Code { get; }
        public string Message { get; }
        public string NodeName { get; }
        public int LineNumber { get; }

        public StoryValidationError(string code, string message, string nodeName = null, int lineNumber = 0)
        {
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            NodeName = nodeName;
            LineNumber = lineNumber;
        }

        public override string ToString()
        {
            string loc = string.IsNullOrEmpty(NodeName) ? string.Empty : $"Node: {NodeName}, ";
            string line = LineNumber > 0 ? $"Line: {LineNumber}, " : string.Empty;
            return $"[{Code}] {loc}{line}{Message}";
        }
    }

    public sealed class StoryValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public List<StoryValidationError> Errors { get; } = new List<StoryValidationError>();
        public List<string> UnreachableNodes { get; } = new List<string>();

        public bool HasError(string code) =>
            Errors.Any(e => string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// ADV 劇情劇本靜態驗證器（C5 查核點驗證器）。
    /// 檢查節點存在性、台詞 ID 唯一性、主資料 ID 引用、分級 Fallback、可達性與自訂指令合法性。
    /// </summary>
    public static class StoryValidator
    {
        public static StoryValidationResult Validate(
            MiniScript.MiniScript script,
            MasterDatabase master = null,
            IEnumerable<string> supportedCommands = null,
            IEnumerable<string> validCgIds = null,
            IEnumerable<string> validEndingIds = null,
            IEnumerable<string> expectedEndingIds = null,
            string entryNode = null)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));

            var result = new StoryValidationResult();
            var commandsSet = new HashSet<string>(
                supportedCommands ?? StoryCommandRegistry.SupportedCommands,
                StringComparer.OrdinalIgnoreCase);

            var cgSet = validCgIds != null ? new HashSet<string>(validCgIds, StringComparer.Ordinal) : null;
            var endingSet = validEndingIds != null ? new HashSet<string>(validEndingIds, StringComparer.Ordinal) : null;
            var seenLineIds = new HashSet<string>(StringComparer.Ordinal);
            var endingLocations = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            // 1. 遍歷所有節點與指令
            foreach (var kvp in script.Nodes)
            {
                string nodeName = kvp.Key;
                MiniScriptNode node = kvp.Value;

                // C5-4: R18 節點 fallback 檢查
                if (node.Rating > Rating.G)
                {
                    if (string.IsNullOrWhiteSpace(node.FallbackNode))
                    {
                        result.Errors.Add(new StoryValidationError(
                            StoryValidationError.R18MissingFallback,
                            $"Node '{nodeName}' has rating '{node.Rating}' but is missing a fallback node.",
                            nodeName));
                    }
                    else if (!script.Nodes.ContainsKey(node.FallbackNode))
                    {
                        result.Errors.Add(new StoryValidationError(
                            StoryValidationError.FallbackNodeNotFound,
                            $"Node '{nodeName}' fallback node '{node.FallbackNode}' does not exist in script.",
                            nodeName));
                    }
                }
                else if (!string.IsNullOrEmpty(node.FallbackNode) && !script.Nodes.ContainsKey(node.FallbackNode))
                {
                    result.Errors.Add(new StoryValidationError(
                        StoryValidationError.FallbackNodeNotFound,
                        $"Node '{nodeName}' fallback node '{node.FallbackNode}' does not exist in script.",
                        nodeName));
                }

                foreach (MiniScriptInstruction inst in node.Instructions)
                {
                    // C5-2: 台詞 line ID 檢查
                    if (inst is LineInstruction lineInst)
                    {
                        if (string.IsNullOrWhiteSpace(lineInst.LineId) || lineInst.LineId == "-")
                        {
                            result.Errors.Add(new StoryValidationError(
                                StoryValidationError.MissingLineId,
                                "Line instruction is missing a valid line ID.",
                                nodeName,
                                lineInst.LineNumber));
                        }
                        else if (!seenLineIds.Add(lineInst.LineId))
                        {
                            result.Errors.Add(new StoryValidationError(
                                StoryValidationError.DuplicateLineId,
                                $"Duplicate line ID '{lineInst.LineId}'.",
                                nodeName,
                                lineInst.LineNumber));
                        }
                        continue;
                    }

                    // C5-1: Target node 存在性檢查
                    if (inst is JumpInstruction jumpInst)
                    {
                        if (!script.Nodes.ContainsKey(jumpInst.TargetNode))
                        {
                            result.Errors.Add(new StoryValidationError(
                                StoryValidationError.TargetNodeNotFound,
                                $"Jump target node '{jumpInst.TargetNode}' does not exist in script.",
                                nodeName,
                                jumpInst.LineNumber));
                        }
                        continue;
                    }

                    if (inst is IfInstruction ifInst)
                    {
                        if (!script.Nodes.ContainsKey(ifInst.TargetNode))
                        {
                            result.Errors.Add(new StoryValidationError(
                                StoryValidationError.TargetNodeNotFound,
                                $"If target node '{ifInst.TargetNode}' does not exist in script.",
                                nodeName,
                                ifInst.LineNumber));
                        }
                        continue;
                    }

                    if (inst is OptionInstruction optInst)
                    {
                        if (!script.Nodes.ContainsKey(optInst.TargetNode))
                        {
                            result.Errors.Add(new StoryValidationError(
                                StoryValidationError.TargetNodeNotFound,
                                $"Option target node '{optInst.TargetNode}' does not exist in script.",
                                nodeName,
                                optInst.LineNumber));
                        }
                        continue;
                    }

                    // 指令檢查 (C5-3, C5-6)
                    if (inst is CommandInstruction cmdInst)
                    {
                        // C5-6: 指令名稱合法性
                        if (!commandsSet.Contains(cmdInst.CommandName))
                        {
                            result.Errors.Add(new StoryValidationError(
                                StoryValidationError.UnregisteredCommand,
                                $"Unregistered story command '{cmdInst.CommandName}'.",
                                nodeName,
                                cmdInst.LineNumber));
                            continue;
                        }

                        string[] args = cmdInst.Arguments;
                        string cmd = cmdInst.CommandName.ToLowerInvariant();

                        // 統計結局位置供 C5-5 分析
                        if (cmd == "ending" && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
                        {
                            string endId = args[0];
                            if (!endingLocations.TryGetValue(endId, out var locs))
                            {
                                locs = new List<string>();
                                endingLocations[endId] = locs;
                            }
                            locs.Add(nodeName);
                        }

                        // C5-3: 主資料與 ID 引用檢查
                        if (master != null)
                        {
                            switch (cmd)
                            {
                                case "show":
                                case "hide":
                                case "expr":
                                case "motion":
                                case "affection":
                                    if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
                                    {
                                        string charId = args[0];
                                        if (!master.Characters.ContainsKey(charId))
                                        {
                                            result.Errors.Add(new StoryValidationError(
                                                StoryValidationError.InvalidCharacterId,
                                                $"Referenced character '{charId}' does not exist in MasterDatabase.",
                                                nodeName,
                                                cmdInst.LineNumber));
                                        }
                                    }
                                    break;

                                case "bg":
                                    if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
                                    {
                                        string sceneId = args[0];
                                        if (!master.Scenes.ContainsKey(sceneId))
                                        {
                                            result.Errors.Add(new StoryValidationError(
                                                StoryValidationError.InvalidSceneId,
                                                $"Referenced scene '{sceneId}' does not exist in MasterDatabase.",
                                                nodeName,
                                                cmdInst.LineNumber));
                                        }
                                    }
                                    break;
                            }
                        }

                        // CG ID 檢查
                        if (cmd == "cg" && cgSet != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
                        {
                            string cgId = args[0];
                            if (!cgSet.Contains(cgId))
                            {
                                result.Errors.Add(new StoryValidationError(
                                    StoryValidationError.InvalidCgId,
                                    $"Referenced CG '{cgId}' is not defined in valid CGs.",
                                    nodeName,
                                    cmdInst.LineNumber));
                            }
                        }

                        // Ending ID 檢查
                        if (cmd == "ending" && endingSet != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
                        {
                            string endId = args[0];
                            if (!endingSet.Contains(endId))
                            {
                                result.Errors.Add(new StoryValidationError(
                                    StoryValidationError.InvalidEndingId,
                                    $"Referenced ending '{endId}' is not defined in valid Endings.",
                                    nodeName,
                                    cmdInst.LineNumber));
                            }
                        }
                    }
                }
            }

            // 2. C5-5: 可達性分析 (Reachability Analysis)
            string actualEntry = entryNode;
            if (string.IsNullOrEmpty(actualEntry))
            {
                if (script.Nodes.ContainsKey("Start")) actualEntry = "Start";
                else if (script.Nodes.ContainsKey("Main")) actualEntry = "Main";
                else actualEntry = script.Nodes.Keys.FirstOrDefault();
            }

            var reachableNodes = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(actualEntry) && script.Nodes.ContainsKey(actualEntry))
            {
                var queue = new Queue<string>();
                queue.Enqueue(actualEntry);
                reachableNodes.Add(actualEntry);

                while (queue.Count > 0)
                {
                    string cur = queue.Dequeue();
                    if (!script.Nodes.TryGetValue(cur, out MiniScriptNode curNode))
                        continue;

                    // Fallback 路徑亦為可達
                    if (!string.IsNullOrEmpty(curNode.FallbackNode) && script.Nodes.ContainsKey(curNode.FallbackNode))
                    {
                        if (reachableNodes.Add(curNode.FallbackNode))
                        {
                            queue.Enqueue(curNode.FallbackNode);
                        }
                    }

                    foreach (MiniScriptInstruction inst in curNode.Instructions)
                    {
                        if (inst is JumpInstruction j && script.Nodes.ContainsKey(j.TargetNode))
                        {
                            if (reachableNodes.Add(j.TargetNode)) queue.Enqueue(j.TargetNode);
                        }
                        else if (inst is IfInstruction ifI && script.Nodes.ContainsKey(ifI.TargetNode))
                        {
                            if (reachableNodes.Add(ifI.TargetNode)) queue.Enqueue(ifI.TargetNode);
                        }
                        else if (inst is OptionInstruction opt && script.Nodes.ContainsKey(opt.TargetNode))
                        {
                            if (reachableNodes.Add(opt.TargetNode)) queue.Enqueue(opt.TargetNode);
                        }
                    }
                }
            }

            // 統計不可達節點
            foreach (string n in script.Nodes.Keys)
            {
                if (!reachableNodes.Contains(n))
                {
                    result.UnreachableNodes.Add(n);
                }
            }

            // 檢查結局可達性
            // 2.1 檢查期望的結局
            if (expectedEndingIds != null)
            {
                foreach (string expected in expectedEndingIds)
                {
                    if (!endingLocations.TryGetValue(expected, out var locs))
                    {
                        result.Errors.Add(new StoryValidationError(
                            StoryValidationError.UnreachableEnding,
                            $"Expected ending '{expected}' is never defined in any node."));
                    }
                    else if (locs.All(loc => !reachableNodes.Contains(loc)))
                    {
                        result.Errors.Add(new StoryValidationError(
                            StoryValidationError.UnreachableEnding,
                            $"Expected ending '{expected}' is defined in unreachable node(s): {string.Join(", ", locs)}."));
                    }
                }
            }

            // 2.2 檢查腳本內部定義的結局是否有完全不可達者
            foreach (var kvp in endingLocations)
            {
                string endId = kvp.Key;
                List<string> locs = kvp.Value;
                if (locs.Count > 0 && locs.All(loc => !reachableNodes.Contains(loc)))
                {
                    // 若前面未因 expected 重複回報，則在此回報
                    if (expectedEndingIds == null || !expectedEndingIds.Contains(endId))
                    {
                        result.Errors.Add(new StoryValidationError(
                            StoryValidationError.UnreachableEnding,
                            $"Ending '{endId}' is defined only in unreachable node(s): {string.Join(", ", locs)}."));
                    }
                }
            }

            return result;
        }
    }
}
