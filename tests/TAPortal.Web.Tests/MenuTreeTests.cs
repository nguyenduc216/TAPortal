using TAPortal.Web.Models;
using TAPortal.Web.Services;
using Xunit;

namespace TAPortal.Web.Tests;

public sealed class MenuTreeTests
{
    [Fact]
    public void BuildMenuTree_PreservesArbitraryDepthAndStableOrder()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var leafId = Guid.NewGuid();
        var siblingId = Guid.NewGuid();
        var menus = new[]
        {
            Menu(leafId, childId, "LEAF", "/leaf", 1),
            Menu(siblingId, rootId, "SIBLING", "/sibling", 20),
            Menu(rootId, null, "ROOT", null, 10),
            Menu(childId, rootId, "CHILD", null, 10)
        };

        var tree = PortalDb.BuildMenuTree(menus);

        var root = Assert.Single(tree);
        Assert.Equal(rootId, root.Menu.Id);
        Assert.Equal(new[] { childId, siblingId }, root.Children.Select(x => x.Menu.Id));
        Assert.Equal(leafId, Assert.Single(root.Children[0].Children).Menu.Id);
    }

    [Fact]
    public void BuildMenuTree_RemovesEmptyNonNavigableContainer()
    {
        var tree = PortalDb.BuildMenuTree(new[] { Menu(Guid.NewGuid(), null, "EMPTY", null, 1) });

        Assert.Empty(tree);
    }

    [Fact]
    public void BuildMenuTree_KeepsNavigableOrphanAsRoot()
    {
        var orphan = Menu(Guid.NewGuid(), Guid.NewGuid(), "ORPHAN", "/orphan", 1);

        var root = Assert.Single(PortalDb.BuildMenuTree(new[] { orphan }));

        Assert.Equal(orphan.Id, root.Menu.Id);
    }

    private static PortalMenu Menu(Guid id, Guid? parentId, string code, string? route, int sortOrder) =>
        new(id, parentId, code, code, null, route, sortOrder, true, true);
}
