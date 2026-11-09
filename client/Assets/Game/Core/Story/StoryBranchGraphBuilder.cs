using System;
using System.Collections.Generic;
using Game.Core.Story.MiniScript;
using Game.Domain;

namespace Game.Core.Story
{
    /// <summary>
    /// 劇本分支拓樸節點模型 (M3-6)
    /// </summary>
    public sealed class StoryBranchNode
    {
        public string NodeId { get; set; }
        public bool IsVisited { get; set; }
        public bool IsEnding { get; set; }
        public string EndingId { get; set; }
        public Rating Rating { get; set; } = Rating.G;

        public StoryBranchNode(string nodeId)
        {
            NodeId = nodeId ?? string.Empty;
        }
    }

    /// <summary>
    /// 劇本分支有向邊模型 (M3-6)
    /// </summary>
    public sealed class StoryBranchEdge
    {
        public string FromNode { get; set; }
        public string ToNode { get; set; }
        public string OptionTextKey { get; set; }
        public bool IsTaken { get; set; }

        public StoryBranchEdge(string fromNode, string toNode, string optionTextKey = null, bool isTaken = false)
        {
            FromNode = fromNode ?? string.Empty;
            ToNode = toNode ?? string.Empty;
            OptionTextKey = optionTextKey;
            IsTaken = isTaken;
        }
    }

    /// <summary>
    /// 劇本分支拓樸圖 (M3-6)
    /// </summary>
    public sealed class StoryBranchGraph
    {
        public List<StoryBranchNode> Nodes { get; } = new List<StoryBranchNode>();
        public List<StoryBranchEdge> Edges { get; } = new List<StoryBranchEdge>();

        public int TotalNodes => Nodes.Count;
        public int VisitedNodes
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Nodes.Count; i++)
                {
                    if (Nodes[i].IsVisited) count++;
                }
                return count;
            }
        }

        public float CompletionRate => TotalNodes > 0 ? (float)VisitedNodes / TotalNodes : 0f;

        public StoryBranchNode FindNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return null;
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (string.Equals(Nodes[i].NodeId, nodeId, StringComparison.Ordinal))
                    return Nodes[i];
            }
            return null;
        }

        public List<StoryBranchEdge> GetOutgoingEdges(string nodeId)
        {
            var result = new List<StoryBranchEdge>();
            if (string.IsNullOrEmpty(nodeId)) return result;
            for (int i = 0; i < Edges.Count; i++)
            {
                if (string.Equals(Edges[i].FromNode, nodeId, StringComparison.Ordinal))
                    result.Add(Edges[i]);
            }
            return result;
        }
    }

    /// <summary>
    /// 劇本分支拓樸圖建構器 (M3-6)
    /// </summary>
    public static class StoryBranchGraphBuilder
    {
        public static StoryBranchGraph Build(string scriptText, ISet<string> readNodes = null, IDictionary<string, string> endings = null)
        {
            if (string.IsNullOrEmpty(scriptText)) return new StoryBranchGraph();
            var script = MiniScript.MiniScript.Parse(scriptText);
            return Build(script, readNodes, endings);
        }

        public static StoryBranchGraph Build(MiniScript.MiniScript script, ISet<string> readNodes = null, IDictionary<string, string> endings = null)
        {
            var graph = new StoryBranchGraph();
            if (script == null || script.Nodes == null) return graph;

            foreach (var kvp in script.Nodes)
            {
                var miniNode = kvp.Value;
                bool isVisited = readNodes != null && readNodes.Contains(miniNode.Name);

                bool isEnding = false;
                string endingId = null;

                bool hasOutgoing = false;
                for (int i = 0; i < miniNode.Instructions.Count; i++)
                {
                    var inst = miniNode.Instructions[i];
                    if (inst is OptionInstruction opt)
                    {
                        hasOutgoing = true;
                        bool taken = readNodes != null && readNodes.Contains(opt.TargetNode);
                        graph.Edges.Add(new StoryBranchEdge(miniNode.Name, opt.TargetNode, opt.TextKey, taken));
                    }
                    else if (inst is JumpInstruction jump)
                    {
                        hasOutgoing = true;
                        bool taken = readNodes != null && readNodes.Contains(jump.TargetNode);
                        graph.Edges.Add(new StoryBranchEdge(miniNode.Name, jump.TargetNode, null, taken));
                    }
                    else if (inst is IfInstruction ifInst)
                    {
                        hasOutgoing = true;
                        bool taken = readNodes != null && readNodes.Contains(ifInst.TargetNode);
                        graph.Edges.Add(new StoryBranchEdge(miniNode.Name, ifInst.TargetNode, ifInst.Condition, taken));
                    }
                    else if (inst is CommandInstruction cmd && string.Equals(cmd.CommandName, "ending", StringComparison.OrdinalIgnoreCase))
                    {
                        isEnding = true;
                        if (cmd.Arguments != null && cmd.Arguments.Length > 0)
                        {
                            endingId = cmd.Arguments[0];
                        }
                    }
                    else if (inst is EndInstruction)
                    {
                        isEnding = true;
                    }
                }

                if (!hasOutgoing)
                {
                    isEnding = true;
                }

                var branchNode = new StoryBranchNode(miniNode.Name)
                {
                    IsVisited = isVisited,
                    IsEnding = isEnding,
                    EndingId = endingId,
                    Rating = miniNode.Rating
                };

                graph.Nodes.Add(branchNode);
            }

            return graph;
        }
    }
}
