using Skylab.Forms.Domain.Workflows;

namespace Skylab.Forms.Tests.Domain.Workflows;

public class WorkflowStepOrderTests
{
    [Fact]
    public void Steps_follow_their_longest_route_and_unreachable_ones_come_last()
    {
        // c'ye a'dan kısayol olsa da en uzun rota a → b → c olduğu için c b'den sonra gelir.
        var graph = new GraphBuilder();
        var orphan = graph.Node("orphan");
        var c = graph.Node("c");
        var a = graph.Node("a", start: true);
        var b = graph.Node("b");
        graph.Route(a, b);
        graph.Route(a, c);
        graph.Route(b, c);

        var order = WorkflowStepOrder.Arrange(graph.Nodes, graph.Transitions);

        Assert.Equal(new[] { ("a", 1), ("b", 2), ("c", 3), ("orphan", 0) }, order.Select(step => (step.Node.NodeKey, step.Depth)));
    }
}
