using System;
using System.Collections.Generic;

namespace OpenCAD2D.Plugins
{
    /// <summary>
    /// One command a <c>plugin.json</c> declares, written as a name (<c>"RING"</c>) or as an object that says more:
    /// <c>{ "name": "RING", "alias": "RG", "description": "Draws a ring", "usesSelection": true }</c>. A Python tool declares its
    /// commands here so that the app shows them, and starts the tool's process only when one is used.
    /// </summary>
    public sealed class CadPluginCommandEntry
    {
        internal CadPluginCommandEntry(string name, string alias, string description, bool usesSelection)
        {
            Name = name;
            Alias = alias;
            Description = description;
            UsesSelection = usesSelection;
        }

        /// <summary>Gets the name of the command, in capitals.</summary>
        public string Name { get; }

        /// <summary>Gets the alias typed in place of the name, in capitals; null where none is declared.</summary>
        public string Alias { get; }

        /// <summary>Gets what the command does, in a line; null where none is declared.</summary>
        public string Description { get; }

        /// <summary>Gets whether the command takes the objects selected before it starts, as MOVE does.</summary>
        public bool UsesSelection { get; }
    }

    /// <summary>One tab of the <c>"ribbon"</c> of a <c>plugin.json</c>: <c>{ "id": "tools", "title": "Tools", "panels": [ ... ] }</c>.</summary>
    public sealed class CadPluginRibbonTabEntry
    {
        internal CadPluginRibbonTabEntry(string id, string title, IReadOnlyList<CadPluginRibbonPanelEntry> panels)
        {
            Id = id;
            Title = title;
            Panels = panels;
        }

        /// <summary>Gets the tab's identity, unique among the loaded plugins; the title where none is declared.</summary>
        public string Id { get; }

        /// <summary>Gets the title on the tab strip.</summary>
        public string Title { get; }

        /// <summary>Gets the panels, left to right.</summary>
        public IReadOnlyList<CadPluginRibbonPanelEntry> Panels { get; }
    }

    /// <summary>One panel of a tab of the <c>"ribbon"</c> of a <c>plugin.json</c>: <c>{ "title": "Draw", "buttons": [ ... ] }</c>.</summary>
    public sealed class CadPluginRibbonPanelEntry
    {
        internal CadPluginRibbonPanelEntry(string title, IReadOnlyList<CadPluginRibbonButtonEntry> buttons)
        {
            Title = title;
            Buttons = buttons;
        }

        /// <summary>Gets the title under the panel.</summary>
        public string Title { get; }

        /// <summary>Gets the buttons, left to right.</summary>
        public IReadOnlyList<CadPluginRibbonButtonEntry> Buttons { get; }
    }

    /// <summary>
    /// One button of a panel of the <c>"ribbon"</c> of a <c>plugin.json</c>:
    /// <c>{ "text": "Ring", "command": "RING", "icon": "ring.png", "size": "large", "tooltip": "Draws a ring" }</c>. A button starts a
    /// command and does nothing else.
    /// </summary>
    public sealed class CadPluginRibbonButtonEntry
    {
        internal CadPluginRibbonButtonEntry(string text, string command, string icon, bool isLarge, string tooltip)
        {
            Text = text;
            Command = command;
            Icon = icon;
            IsLarge = isLarge;
            ToolTip = tooltip;
        }

        /// <summary>Gets the text of the button.</summary>
        public string Text { get; }

        /// <summary>Gets the command the button starts.</summary>
        public string Command { get; }

        /// <summary>Gets the path of the button's icon, relative to the manifest; null where none is declared.</summary>
        public string Icon { get; }

        /// <summary>Gets whether the button is drawn large (<c>"size": "large"</c>, the icon 32 pixels over the text); false for the standard size.</summary>
        public bool IsLarge { get; }

        /// <summary>Gets the tooltip of the button; null where none is declared.</summary>
        public string ToolTip { get; }
    }
}
