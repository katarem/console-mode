using Wpf.Ui.Abstractions;

namespace ConsoleMode.UI.Fluent;

/// <summary>Serves the window's pages, created once, to the navigation view.</summary>
internal sealed class PageProvider : INavigationViewPageProvider
{
    private readonly Dictionary<Type, object> _pages;

    public PageProvider(IEnumerable<object> pages) => _pages = pages.ToDictionary(page => page.GetType());

    public object? GetPage(Type pageType) => _pages.GetValueOrDefault(pageType);
}
