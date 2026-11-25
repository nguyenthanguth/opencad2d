using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Plugins;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The object snaps of one Python tool: the finder the window asks, which never waits for the tool, and what it answers from.
    /// The tool works out the points of an entity in its own process, so a finder asked about an entity it has not been asked
    /// about before answers none, notes the question, and the misses of one pass of the snap, every keyword of the tool together,
    /// go to the tool in one <c>snap</c> message sent by one post to the window's thread after the pass. The answers come back on
    /// the tool's control pipe (<see cref="Answered"/>), are kept per drawing, entity, keyword and block references until the
    /// drawing changes, and the canvas is told to follow the cursor again (<see cref="CadCanvas.Retrack"/>), so that the point shows
    /// with no other move. A point typed after the snap's keyword is the one deliberate act: it sends its question at once and
    /// waits for the answer on the window's thread, <see cref="TypedWait"/> at most. A failure the tool answers for an entity is
    /// thrown at the snap's next ask, which switches the snap off for the session as a finder that throws does.
    /// </summary>
    /// <remarks>
    /// <see cref="FinderOf"/> and the flush run on the window's thread. <see cref="Answered"/> runs on the thread that reads the
    /// tool's control pipe and touches nothing of the window's: it keeps the answers under the lock the finder looks them up
    /// under and wakes a typed point that waits.
    /// </remarks>
    internal sealed class CadToolSnaps
    {
        /// <summary>How long a point typed after the snap's keyword waits for the tool: 1 second.</summary>
        internal static readonly TimeSpan TypedWait = TimeSpan.FromSeconds(1);

        /// <summary>The most entries the cache of a tool holds; the next one clears it whole.</summary>
        internal const int MaxEntries = 10000;

        private enum State
        {
            Queued,
            Asked,
            Answered,
            Failed,
        }

        private sealed class Entry
        {
            public State State;
            public GeoPoint2[] Points = Array.Empty<GeoPoint2>();
            public string Keyword;
        }

        /// <summary>What is known of one drawing: its entries, and which generation of the drawing they are of.</summary>
        private sealed class Cache
        {
            public readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            public int Generation;
        }

        /// <summary>One entity not yet asked about, the entry it fills and what it was when it was asked of.</summary>
        private sealed class Pending
        {
            public CadDocument Document;
            public Cache Cache;
            public int Generation;
            public Entry Entry;
            public string Keyword;
            public DxfEntity Entity;
            public string Handle;
            public IReadOnlyList<string> Blocks;
        }

        private sealed class ReferenceComparer : IEqualityComparer<DxfEntity>
        {
            public bool Equals(DxfEntity a, DxfEntity b) => ReferenceEquals(a, b);

            public int GetHashCode(DxfEntity entity) => RuntimeHelpers.GetHashCode(entity);
        }

        private readonly string _tool;
        private readonly Func<string, bool> _send;
        private readonly Action<Action> _post;
        private readonly Action _start;
        private readonly Action<IReadOnlyList<CadDocument>> _answered;
        private readonly object _gate = new object();
        private readonly ConditionalWeakTable<CadDocument, Cache> _caches =
            new ConditionalWeakTable<CadDocument, Cache>();
        private readonly List<WeakReference> _live = new List<WeakReference>();
        private readonly List<Pending> _pending = new List<Pending>();
        private readonly Dictionary<long, List<Pending>> _asked = new Dictionary<long, List<Pending>>();
        private readonly List<string> _unsent = new List<string>();
        private readonly List<string> _declared = new List<string>();
        private readonly Dictionary<string, string> _failures = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<CadDocument> _answeredDocuments = new List<CadDocument>();
        private int _entries;
        private long _nextId;
        private int _epoch;
        private bool _flushPosted;
        private bool _answerPosted;
        private bool _closed;
        private long _silentUntil;

        /// <summary>Makes the snaps of a tool.</summary>
        /// <param name="tool">The tool's name, as lines about it say it.</param>
        /// <param name="send">Sends a line on the tool's control pipe; false where the tool has no process to take it, when the line waits for <see cref="ProcessStarted"/>. Called on any thread.</param>
        /// <param name="post">Runs an action on the window's thread after the work in hand, at Background priority.</param>
        /// <param name="start">Starts the tool's process where there is none and it may be started without asking anything (see <see cref="PythonEnvironments.Prepare"/>); called on the window's thread, it must not wait.</param>
        /// <param name="answered">Called on the window's thread with the drawings an answer came for, once for the answers that came since it was last called.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        internal CadToolSnaps(
            string tool,
            Func<string, bool> send,
            Action<Action> post,
            Action start,
            Action<IReadOnlyList<CadDocument>> answered
        )
        {
            _tool = tool ?? throw new ArgumentNullException(nameof(tool));
            _send = send ?? throw new ArgumentNullException(nameof(send));
            _post = post ?? throw new ArgumentNullException(nameof(post));
            _start = start ?? throw new ArgumentNullException(nameof(start));
            _answered = answered ?? throw new ArgumentNullException(nameof(answered));
        }

        /// <summary>Gets the keywords of the snaps given a finder, in capitals, in the order they were given.</summary>
        internal IReadOnlyList<string> Declared
        {
            get
            {
                lock (_gate)
                {
                    return _declared.ToArray();
                }
            }
        }

        /// <summary>Gets how many entries the cache holds.</summary>
        internal int Count
        {
            get
            {
                lock (_gate)
                {
                    return _entries;
                }
            }
        }

        /// <summary>
        /// Makes the finder of a snap a manifest declares. It answers none, with no question, for an entity of a type the snap does
        /// not name (the DXF names of <see cref="CadPluginSnapEntry.EntityTypes"/>, any entity where it names none), and for an
        /// entity with no handle or no drawing; otherwise the points the tool gave for it, or none while it has not.
        /// </summary>
        /// <param name="entry">The snap as plugin.json declares it.</param>
        /// <returns>The finder.</returns>
        internal CadSnapFinder FinderOf(CadPluginSnapEntry entry)
        {
            string keyword = entry.Keyword;
            string[] types = entry.EntityTypes.ToArray();

            lock (_gate)
            {
                if (!_declared.Contains(keyword))
                {
                    _declared.Add(keyword);
                }
            }

            return request => Find(keyword, types, request);
        }

        private IEnumerable<GeoPoint2> Find(string keyword, string[] types, CadSnapRequest request)
        {
            Throw(keyword);
            DxfEntity entity = request.Entity;
            CadDocument document = request.Document;

            if (
                _closed
                || entity == null
                || document == null
                || string.IsNullOrEmpty(request.Handle)
                || (types.Length > 0 && Array.IndexOf(types, entity.DxfName) < 0)
            )
            {
                return null;
            }

            string key = keyword + "|" + request.Handle + "|" + string.Join("/", request.BlockReferences);
            Entry entry;
            bool asked = false;
            int epoch;

            lock (_gate)
            {
                epoch = _epoch;
                Cache cache = CacheOf(document);

                if (!cache.Entries.TryGetValue(key, out entry))
                {
                    if (_entries >= MaxEntries)
                    {
                        Reset();
                        cache = CacheOf(document);
                    }

                    entry = new Entry { Keyword = keyword };
                    cache.Entries.Add(key, entry);
                    _entries++;
                    _pending.Add(
                        new Pending
                        {
                            Document = document,
                            Cache = cache,
                            Generation = cache.Generation,
                            Entry = entry,
                            Keyword = keyword,
                            Entity = entity,
                            Handle = request.Handle,
                            Blocks = request.BlockReferences,
                        }
                    );
                    asked = true;
                }
            }

            if (asked)
            {
                if (request.IsTyped)
                {
                    Flush();
                }
                else
                {
                    Post();
                }
            }

            if (request.IsTyped)
            {
                WaitFor(entry, epoch);
            }

            lock (_gate)
            {
                if (entry.State == State.Failed && request.IsTyped)
                {
                    Throw(keyword);
                }

                return entry.State == State.Answered ? entry.Points : null;
            }
        }

        /// <summary>Throws the failure the tool answered for the snap, once: the entries that failed are dropped, so that the snap, chosen on again, asks again.</summary>
        private void Throw(string keyword)
        {
            string failure;

            lock (_gate)
            {
                if (!_failures.TryGetValue(keyword, out failure))
                {
                    return;
                }

                _failures.Remove(keyword);

                foreach (Cache cache in Caches())
                {
                    foreach (
                        string key in cache
                            .Entries.Where(e => e.Value.State == State.Failed && e.Value.Keyword == keyword)
                            .Select(e => e.Key)
                            .ToList()
                    )
                    {
                        cache.Entries.Remove(key);
                        _entries--;
                    }
                }
            }

            throw new InvalidOperationException(failure);
        }

        /// <summary>Waits, on the window's thread, until the tool has answered an entry, the cache is cleared, or <see cref="TypedWait"/> has passed; a wait that ran out lets the next typed point of the same moment go by without waiting.</summary>
        private void WaitFor(Entry entry, int epoch)
        {
            lock (_gate)
            {
                long now = Stopwatch.GetTimestamp();

                if (now < _silentUntil)
                {
                    return;
                }

                long end = now + (long)(TypedWait.TotalSeconds * Stopwatch.Frequency);

                while (_epoch == epoch && !_closed && (entry.State == State.Queued || entry.State == State.Asked))
                {
                    long left = end - Stopwatch.GetTimestamp();

                    if (left <= 0)
                    {
                        _silentUntil = end + (long)(TypedWait.TotalSeconds * Stopwatch.Frequency);
                        return;
                    }

                    Monitor.Wait(_gate, TimeSpan.FromSeconds((double)left / Stopwatch.Frequency));
                }
            }
        }

        private void Post()
        {
            lock (_gate)
            {
                if (_flushPosted)
                {
                    return;
                }

                _flushPosted = true;
            }

            _post(() =>
            {
                lock (_gate)
                {
                    _flushPosted = false;
                }

                Flush();
            });
        }

        // ---- The question ---------------------------------------------------------------------------------------------

        /// <summary>Sends the questions noted since the last flush, as one message; on the window's thread. An entry whose drawing changed since it was noted is not asked.</summary>
        internal void Flush()
        {
            List<Pending> items;

            lock (_gate)
            {
                items = _pending.Where(p => p.Generation == p.Cache.Generation).ToList();
                _pending.Clear();
            }

            if (items.Count == 0)
            {
                return;
            }

            var references = new Dictionary<DxfEntity, string>(new ReferenceComparer());
            string fragment = Pack(items, references);
            var sent = new List<Pending>();
            var list = new JsonArray();

            foreach (Pending item in items)
            {
                if (references.TryGetValue(item.Entity, out string reference))
                {
                    sent.Add(item);
                    list.Add(
                        new JsonObject()
                            .Set("keyword", item.Keyword)
                            .Set("ref", reference)
                            .Set("handle", item.Handle)
                            .Set("blocks", new JsonArray(item.Blocks))
                    );
                }
                else
                {
                    // An entity that cannot be put in the small drawing has no points, and is not asked again until the drawing changes.
                    lock (_gate)
                    {
                        item.Entry.State = State.Answered;
                    }
                }
            }

            if (sent.Count == 0)
            {
                lock (_gate)
                {
                    Monitor.PulseAll(_gate);
                }

                return;
            }

            string line;

            lock (_gate)
            {
                long id = ++_nextId;
                _asked[id] = sent;

                foreach (Pending item in sent)
                {
                    item.Entry.State = State.Asked;
                }

                line = RemoteJson.Write(
                    new JsonObject().Set("op", "snap").Set("id", id).Set("fragment", fragment).Set("items", list)
                );
            }

            if (!_send(line))
            {
                lock (_gate)
                {
                    _unsent.Add(line);
                }

                _start();
            }
        }

        /// <summary>The entities of the items in a small drawing, as the channel packs them (<see cref="RemoteFragment"/>): the text of it, and for each entity the handle it has in it.</summary>
        private static string Pack(List<Pending> items, Dictionary<DxfEntity, string> references)
        {
            var fragment = new DxfDatabase(RemoteFragment.Version);

            foreach (IGrouping<CadDocument, Pending> group in items.GroupBy(i => i.Document))
            {
                // The entities of the drawing's spaces are copied together, as the channel copies them; one that is a copy placed
                // where a block's references draw it is in no drawing and is cloned in, its layer made there.
                var own = new List<DxfEntity>();
                var placed = new List<DxfEntity>();

                foreach (Pending item in group)
                {
                    if (
                        references.ContainsKey(item.Entity)
                        || own.Contains(item.Entity)
                        || placed.Contains(item.Entity)
                    )
                    {
                        continue;
                    }

                    (
                        item.Blocks.Count == 0 && ReferenceEquals(group.Key.FindEntity(item.Handle), item.Entity)
                            ? own
                            : placed
                    ).Add(item.Entity);
                }

                Copy(group.Key, fragment, own, references);

                foreach (DxfEntity entity in placed)
                {
                    try
                    {
                        DxfEntity clone = entity.Clone();

                        if (!string.IsNullOrEmpty(clone.Layer) && !fragment.LayerTable.Has(clone.Layer))
                        {
                            fragment.LayerTable.Add(new DxfLayerTableRecord { Name = clone.Layer });
                        }

                        fragment.ModelSpace.AppendEntity(clone);
                        references[entity] = clone.Handle;
                    }
                    catch (Exception ex)
                        when (ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException)
                    {
                        GeometryHelperLog.Debug(
                            "CadToolSnaps: " + entity + " could not be put in the question: " + ex.Message
                        );
                    }
                }
            }

            return fragment.ToText();
        }

        private static void Copy(
            CadDocument document,
            DxfDatabase fragment,
            List<DxfEntity> own,
            Dictionary<DxfEntity, string> references
        )
        {
            if (own.Count == 0)
            {
                return;
            }

            IReadOnlyList<DxfEntity> copies = null;

            try
            {
                copies = document.Database.WblockCloneObjects(own, fragment.ModelSpace);
            }
            catch (ArgumentException ex)
            {
                GeometryHelperLog.Debug("CadToolSnaps: the entities were copied one by one: " + ex.Message);
            }

            for (int i = 0; i < own.Count; i++)
            {
                try
                {
                    DxfEntity copy =
                        copies != null
                            ? copies[i]
                            : document.Database.WblockCloneObjects(new[] { own[i] }, fragment.ModelSpace)[0];

                    if (!string.IsNullOrEmpty(copy.Handle) && !copy.IsErased)
                    {
                        references[own[i]] = copy.Handle;
                    }
                }
                catch (ArgumentException ex)
                {
                    GeometryHelperLog.Debug("CadToolSnaps: " + own[i] + " was left out: " + ex.Message);
                }
            }
        }

        // ---- The answer -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Keeps what the tool answered to a question: <c>{"op":"snapped","id":7,"answers":[{"points":[[100,75]]},{"error":"..."}]}</c>,
        /// one answer to each item in their order. An answer for a drawing that has changed since is dropped; one the app cannot
        /// read is an error of every item of the question. Called on the thread that reads the tool's pipe.
        /// </summary>
        /// <param name="message">The message.</param>
        internal void Answered(JsonObject message)
        {
            var documents = new List<CadDocument>();

            lock (_gate)
            {
                long id = message.GetLong("id", -1);

                if (!_asked.TryGetValue(id, out List<Pending> items))
                {
                    return;
                }

                _asked.Remove(id);
                JsonArray answers = message.Get("answers") as JsonArray;
                bool readable = answers != null && answers.Count == items.Count;

                for (int i = 0; i < items.Count; i++)
                {
                    Pending item = items[i];

                    if (item.Generation != item.Cache.Generation)
                    {
                        continue;
                    }

                    Keep(item, readable ? answers[i] as JsonObject : null);

                    if (!documents.Contains(item.Document))
                    {
                        documents.Add(item.Document);
                    }
                }

                Monitor.PulseAll(_gate);
            }

            Announce(documents);
        }

        private void Keep(Pending item, JsonObject answer)
        {
            string error = answer?.GetString("error");

            if (answer != null && error == null && answer.Get("points") is JsonArray points)
            {
                var found = new List<GeoPoint2>(points.Count);

                foreach (object point in points)
                {
                    if (
                        !(point is JsonArray pair)
                        || pair.Count < 2
                        || !TryNumber(pair[0], out double x)
                        || !TryNumber(pair[1], out double y)
                    )
                    {
                        error = item.Keyword + ": the tool gave a point the app cannot read.";
                        break;
                    }

                    found.Add(new GeoPoint2(x, y));
                }

                if (error == null)
                {
                    item.Entry.Points = found.ToArray();
                    item.Entry.State = State.Answered;
                    return;
                }
            }

            item.Entry.State = State.Failed;
            string line = error ?? item.Keyword + ": the app cannot read the answer of the tool " + _tool + ".";
            string prefix = item.Keyword + ": ";
            _failures[item.Keyword] = line.StartsWith(prefix, StringComparison.Ordinal)
                ? line.Substring(prefix.Length)
                : line;
        }

        private static bool TryNumber(object value, out double number)
        {
            number =
                value is double d ? d
                : value is long l ? l
                : double.NaN;
            return !double.IsNaN(number) && !double.IsInfinity(number);
        }

        /// <summary>Tells the window an answer came for a drawing: one post for the answers that come while it is waiting to run.</summary>
        private void Announce(List<CadDocument> documents)
        {
            lock (_gate)
            {
                foreach (CadDocument document in documents)
                {
                    if (!_answeredDocuments.Contains(document))
                    {
                        _answeredDocuments.Add(document);
                    }
                }

                if (_answerPosted || _answeredDocuments.Count == 0)
                {
                    return;
                }

                _answerPosted = true;
            }

            _post(() =>
            {
                List<CadDocument> shown;

                lock (_gate)
                {
                    _answerPosted = false;
                    shown = _answeredDocuments.ToList();
                    _answeredDocuments.Clear();
                }

                _answered(shown);
            });
        }

        // ---- The process ----------------------------------------------------------------------------------------------

        /// <summary>Sends the questions that waited for the tool's process, now it has started; any thread.</summary>
        internal void ProcessStarted()
        {
            List<string> lines;

            lock (_gate)
            {
                lines = _unsent.ToList();
                _unsent.Clear();
            }

            foreach (string line in lines)
            {
                if (!_send(line))
                {
                    lock (_gate)
                    {
                        _unsent.Add(line);
                    }

                    return;
                }
            }
        }

        /// <summary>Forgets everything the tool answered and everything waiting for it, as its process ending does, or its unload, or its reload; a typed point that waits stops waiting.</summary>
        internal void Clear()
        {
            lock (_gate)
            {
                Reset();
            }
        }

        /// <summary>The tool is unloaded: nothing is kept or asked from now on.</summary>
        internal void Close()
        {
            lock (_gate)
            {
                _closed = true;
                Reset();
            }
        }

        /// <summary>Empties the cache and the questions; with the lock held.</summary>
        private void Reset()
        {
            foreach (Cache cache in Caches())
            {
                cache.Entries.Clear();
                cache.Generation++;
            }

            _entries = 0;
            _pending.Clear();
            _asked.Clear();
            _unsent.Clear();
            _failures.Clear();
            _epoch++;
            Monitor.PulseAll(_gate);
        }

        /// <summary>The caches of the drawings that are alive, in the order they were made; with the lock held.</summary>
        private IEnumerable<Cache> Caches()
        {
            var found = new List<Cache>();

            for (int i = 0; i < _live.Count; i++)
            {
                if (_live[i].Target is Cache cache)
                {
                    found.Add(cache);
                }
                else
                {
                    _live.RemoveAt(i--);
                }
            }

            return found;
        }

        /// <summary>The cache of a drawing, made the first time and cleared whenever the drawing changes; with the lock held.</summary>
        private Cache CacheOf(CadDocument document)
        {
            if (_caches.TryGetValue(document, out Cache cache))
            {
                return cache;
            }

            cache = new Cache();
            _caches.Add(document, cache);
            _live.Add(new WeakReference(cache));
            document.Changed += (sender, e) =>
            {
                lock (_gate)
                {
                    _entries -= cache.Entries.Count;
                    cache.Entries.Clear();
                    cache.Generation++;
                }
            };
            return cache;
        }
    }
}
