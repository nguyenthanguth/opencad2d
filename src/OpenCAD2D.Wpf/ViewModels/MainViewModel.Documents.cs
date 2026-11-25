using System.Collections.Generic;

namespace OpenCAD2D.Wpf
{
    /// <summary>The drawings open as a plugin reaches them (<see cref="CadDocuments"/>), through the members the remote channel uses.</summary>
    public sealed partial class MainViewModel
    {
        private CadDocuments _pluginDocuments;

        /// <summary>
        /// Gets the drawings of the window as a plugin's callback and a plugin's command see them: listed, opened, made, shown
        /// and closed by the same code a user's action and a Remote call run. One object serves every session and callback.
        /// </summary>
        internal CadDocuments PluginDocuments =>
            _pluginDocuments ?? (_pluginDocuments = new CadDocuments(new DocumentHost(this), null));

        private sealed class DocumentHost : ICadDocumentHost
        {
            private readonly MainViewModel _model;

            public DocumentHost(MainViewModel model)
            {
                _model = model;
            }

            public IReadOnlyList<CadDocument> Documents => _model.RemoteDocuments();

            public CadDocument ActiveDocument => _model.Document;

            public CadDocument Open(string path) => _model.OpenForRemote(path);

            public CadDocument New(string template) => _model.NewForRemote(template);

            public void Activate(CadDocument document) => _model.ShowDocument(document);

            public CadCommandSession SessionOf(CadDocument document) => _model.SessionOfDocument(document);

            public void Save(CadDocument document) => _model.SaveForRemote(document);

            public void Close(CadDocument document) => _model.CloseForRemote(document);
        }
    }
}
