"""The Drawing API over a ``CadDocument``: a drawing file with no app (headless), and the functions of ``opencad2d.cad`` that
only a tool of the app has, which are the tool backend's in a tool and refuse in any other process.

The commit goes through the public ``CadDocument.ApplyEdit``, as the remote server's commit does
(``CadRemoteServer.Commit.cs``): one undo step with the planned changes asked of the owners of the entities first. Entities and
styles are read as copies in a small drawing of their own.
"""

from __future__ import annotations

import importlib
import os
import re
from collections.abc import Callable
from typing import Any

from . import _render, _tool
from ._drawing import (
    Box,
    Documents,
    Drawing,
    Entity,
    Look,
    Op,
    RenderOptions,
    dxf_type_of,
    look_color,
    names_of,
    provide,
)

_net: Any = importlib.import_module("System")
_cad: Any = importlib.import_module("OpenCAD2D")
_dxf: Any = importlib.import_module("GeometryHelper.Dxf")
_geometry: Any = importlib.import_module("GeometryHelper")
_geo: Any = importlib.import_module("GeometryHelper.Geometry")


_KINDS = ("STYLE", "LTYPE", "BLOCK", "DIMSTYLE")
"""The tables a commit adds records to, in the order they are written: what a record points at comes first."""
_WHAT = ("New text style", "New linetype", "New block", "New dimension style")


def _inside_only(name: str) -> RuntimeError:
    """The error of a function that only a tool of the app can call, called in any other process."""
    return RuntimeError(f"{name} works inside OpenCAD2D only")


def _no_view(name: str) -> RuntimeError:
    """The error of a view call on a drawing of a file, which has no view (API-D26)."""
    return RuntimeError(f"{name} works on a drawing of the app: a drawing file has no view.")


def _refused(error: Any) -> ValueError:
    """What the editor refuses with an ``ArgumentException`` is a ``ValueError`` with its first line, the name of the
    parameter .NET adds left off."""
    first = str(error.Message).splitlines()[0]
    return ValueError(re.sub(r" \(Parameter '[^']*'\)$", "", first))


def _name_key(record: Any) -> tuple[str, str]:
    return str(record.Name).upper(), str(record.Name)


def _handle_key(handle: str) -> tuple[int, str]:
    try:
        return int(handle, 16), handle
    except ValueError:
        return 1 << 62, handle


class _LocalBackend:
    """A ``CadDocument`` of a drawing file as the backend of a ``Drawing``."""

    def __init__(self, document: Any) -> None:
        self._document: Any = document
        self._closed: str | None = None
        self._session: Any = None

    @property
    def document(self) -> Any:
        if self._closed is not None:
            raise RuntimeError(f"The drawing {self._closed} is closed.")
        return self._document

    # ---- what the drawing is ----------------------------------------------------------------------------------------

    @property
    def name(self) -> str:
        return self._closed if self._closed is not None else str(self._document.Title)

    @property
    def path(self) -> str | None:
        file_name = self.document.FileName
        return str(file_name) if file_name is not None and os.path.isabs(str(file_name)) else None

    @property
    def is_active(self) -> bool:
        return True

    @property
    def is_modified(self) -> bool:
        return bool(self.document.IsModified)

    @property
    def net(self) -> Any:
        return self.document

    # ---- reading ----------------------------------------------------------------------------------------------------

    def _spaces(self, layout: str | None) -> list[Any]:
        spaces = [space for space in self.document.Spaces if not space.IsBlockEditor]
        if layout is None:
            return spaces
        found = [space for space in spaces if str(space.Name).upper() == layout.upper()]
        if not found:
            raise ValueError(f"The drawing {self.name} has no layout named {layout!r}.")
        return found

    def _pack(self, entities: list[Any]) -> list[tuple[str, Any, Any]]:
        """Copies of the entities in a small drawing of their own, as the remote server packs what it sends."""
        if not entities:
            return []
        fragment = _dxf.DxfDatabase("AC1027")
        database = self.document.Database
        pairs: list[tuple[Any, Any]] = []
        try:
            copies = database.WblockCloneObjects(_net.Array[_dxf.DxfEntity](entities), fragment.ModelSpace)
            pairs = [(entity, copies[index]) for index, entity in enumerate(entities)]
        except _net.ArgumentException:
            for entity in entities:
                try:
                    copy = database.WblockCloneObjects(_net.Array[_dxf.DxfEntity]([entity]), fragment.ModelSpace)[0]
                    pairs.append((entity, copy))
                except _net.ArgumentException:
                    continue
        return [
            (str(original.Handle), copy, fragment)
            for original, copy in pairs
            if copy.Handle and not copy.IsErased and original.Handle
        ]

    def read(
        self, dxf_type: str | None, layer: str | None, layout: str | None, kind: Any = None
    ) -> list[tuple[str, Any, Any]]:
        found: list[Any] = []
        for space in self._spaces(layout):
            for entity in space.Block:
                if entity.IsErased:
                    continue
                if layer is not None and str(entity.Layer).upper() != layer.upper():
                    continue
                if dxf_type is not None and dxf_type_of(entity).upper() != dxf_type:
                    continue
                if kind is not None and not isinstance(entity, kind):
                    continue
                found.append(entity)
        return self._pack(found)

    def read_one(self, handle: str) -> tuple[Any, Any] | None:
        entity = self.document.FindEntity(handle)
        if entity is None or entity.IsErased or self.document.FindSpace(handle) is None:
            return None
        packed = self._pack([entity])
        return (packed[0][1], packed[0][2]) if packed else None

    def selected(self) -> list[tuple[str, Any, Any]]:
        entities: list[Any] = []
        for handle in sorted((str(h) for h in self.document.Selection.Handles), key=_handle_key):
            entity = self.document.FindEntity(handle)
            if entity is not None and not entity.IsErased:
                entities.append(entity)
        return self._pack(entities)

    def select(self, handles: list[str]) -> None:
        for handle in handles:
            entity = self.document.FindEntity(handle)
            if entity is None or entity.IsErased:
                raise ValueError(f"The drawing {self.name} has no object {handle!r}.")
        self.document.Selection.Set(_net.Array[_net.String](handles))

    # ---- the commit -------------------------------------------------------------------------------------------------

    def _space_named(self, layout: str | None) -> Any:
        if not layout:
            return self.document.ModelSpace
        for space in self._spaces(None):
            if str(space.Name).upper() == layout.upper():
                return space
        raise ValueError(f"The drawing {self.name} has no layout named {layout!r}.")

    def _existing(self, handle: str) -> Any:
        entity = self.document.FindEntity(handle)
        if entity is None or entity.IsErased:
            raise RuntimeError(f"The drawing {self.name} has no object {handle}.")
        if self.document.FindSpace(handle) is None:
            raise RuntimeError(
                f"The object {handle} is inside a block of {self.name}, not in a space, and is not changed from outside."
            )
        return entity

    def names(self, kind: str) -> list[str]:
        return names_of(self.document.Database, kind)

    def _holds(self, records: Any) -> Callable[[str, str], bool]:
        """Whether the drawing has a record of a name, or the commit's small drawing of new ``records`` makes one: for the
        records an object names (its text style, its linetype) to be given to the small drawing it is added to."""
        known: dict[str, set[str]] = {}

        def has(kind: str, name: str) -> bool:
            if kind not in known:
                known[kind] = {n.upper() for n in self.names(kind)}
                if records is not None:
                    known[kind] |= {n.upper() for n in names_of(records, kind)}
            return name.upper() in known[kind]

        return has

    def commit(self, ops: list[Op]) -> list[str | None]:
        document = self.document
        layer_ops = [op for op in ops if op.kind in ("add_layer", "modify_layer")]
        steps = [op for op in ops if op.kind not in ("add_layer", "modify_layer", "records")]
        made = next((op for op in ops if op.kind == "records"), None)
        records = None if made is None else made.records
        has = self._holds(records)
        planned: list[Any] = []
        batch: Any = None
        for op in steps:
            if op.kind == "insert":
                if batch is None:
                    batch = _dxf.DxfDatabase("AC1027")
                copy = op.copy
                # The app draws a dimension's picture; the copy only needs a block of its own to be added with.
                if isinstance(copy, _dxf.DxfDimension) and copy.DimBlock is None:
                    picture = _dxf.DxfBlockTableRecord()
                    picture.Name = "*D"
                    batch.BlockTable.Add(picture)
                    copy.DimBlock = picture
                # The styles, linetype and block the object names are the drawing's, or the commit's own records.
                provide(batch, op.needs, has)
                batch.ModelSpace.AppendEntity(copy)
                self._space_named(op.layout)
            else:
                handle = op.handle if op.kind == "erase" else op.entity.handle  # pyright: ignore[reportOptionalMemberAccess]
                assert handle is not None
                self._existing(handle)
                kind = _cad.CadEditRecordKind.Erased if op.kind == "erase" else _cad.CadEditRecordKind.Reshaped
                planned.append(_cad.CadEditRecord(kind, handle, "Python"))
        inserted: list[str | None] = []

        def apply(scope: Any) -> None:
            inserted.clear()
            if records is not None:
                assert made is not None
                self._write_records(scope, records, made.spec)
            for op in layer_ops:
                assert op.layer is not None
                self._write_layer(scope, op.layer._look, op.kind == "add_layer")  # pyright: ignore[reportPrivateUsage]
            i = 0
            while i < len(steps):
                op = steps[i]
                if op.kind == "insert":
                    space = self._space_named(op.layout)
                    group = [op.copy]
                    i += 1
                    while (
                        i < len(steps)
                        and steps[i].kind == "insert"
                        and str(self._space_named(steps[i].layout).Name) == str(space.Name)
                    ):
                        group.append(steps[i].copy)
                        i += 1
                    self._insert_group(scope, batch, space, group, inserted)
                    continue
                handle = op.handle if op.kind == "erase" else op.entity.handle  # pyright: ignore[reportOptionalMemberAccess]
                assert handle is not None
                if not scope.IsHandled(handle):
                    old = document.FindEntity(handle)
                    if op.kind == "erase":
                        old.Erase()
                    else:
                        self._replace(old, handle, op.entity)  # pyright: ignore[reportArgumentType]
                i += 1

        result = document.ApplyEdit(
            "PYTHON", _net.Array[_cad.CadEditRecord](planned), _net.Action[_cad.CadEditScope](apply)
        )
        if not result.Succeeded:
            raise RuntimeError(str(result.Message))
        return inserted

    def _write_records(self, scope: Any, records: Any, added: dict[str, list[str]]) -> None:
        """Brings the table records the commit made into the drawing (text styles, linetypes, blocks, dimension styles), as the
        app does for a client: those the drawing has not, each told to the edit."""
        have = {kind: {n.upper() for n in self.names(kind)} for kind in _KINDS}
        new = {kind: [n for n in added[kind] if n.upper() not in have[kind]] for kind in _KINDS}
        _cad.CadTableRecords.Add(self.document.Database, records, *(_net.Array[_net.String](new[kind]) for kind in _KINDS))
        for kind, what in zip(_KINDS, _WHAT):
            for name in new[kind]:
                scope.Record(_cad.CadEditRecord(_cad.CadEditRecordKind.TableChanged, None, what, None, name))

    def edit(
        self, ops: list[Op], how: str, items: list[Entity], args: tuple[Any, ...]
    ) -> tuple[list[str | None], list[tuple[str, Any, Any]]]:
        """What is queued and one call of the ``CadEditor`` as one undo step, PYTHON, or inside the group the script has open.
        The edit is checked first by the editor's own dry run (``CadEditor.Check``: its numbers, its objects, its plan and the
        owner's gate), so that a refused edit applies none of what was queued with it. What the editor can only find in the
        writing (an owner that does not take the edit itself) cancels the group of its own and, inside the script's, is the
        refusal of any commit."""
        document = self.document
        handles = list(dict.fromkeys(item.handle for item in items))
        entities = [self._found(handle) for handle in handles]
        self._check(how, entities, args)
        own = not document.IsInGroup
        if own:
            document.BeginGroup("PYTHON")
        try:
            inserted = self.commit(ops) if ops else []
            made = self._edited(how, [document.FindEntity(handle) for handle in handles], args)
        except BaseException:
            if own:
                document.CancelGroup()
            raise
        if own:
            document.EndGroup()
        return inserted, self._pack(made)

    def _found(self, handle: str) -> Any:
        """The object of the drawing that has the handle, which the editor is given; it is the editor that refuses one that is
        erased, inside a block or on a locked layer."""
        entity = self.document.FindEntity(handle)
        if entity is None:
            raise RuntimeError(f"The drawing {self.name} has no object {handle}.")
        return entity

    def _check(self, how: str, entities: list[Any], args: tuple[Any, ...]) -> None:
        """Runs the edit through ``CadEditor.Check``, which makes every check the call makes and writes nothing, and raises what
        the call would raise. The exception is caught inside the editor's callback and raised here, so that none crosses it."""
        refused: list[Exception] = []

        def probe(editor: Any) -> None:
            try:
                self._edited(how, entities, args, editor)
            except (ValueError, RuntimeError) as error:
                refused.append(error)

        self._editor.Check(_net.Action[_cad.CadEditor](probe))
        if refused:
            raise refused[0]

    def _edited(self, how: str, entities: list[Any], args: tuple[Any, ...], editor: Any = None) -> list[Any]:
        """One call of the editor (the session's, or the one a check hands over) for an edit, its refusals as Python's:
        ``ValueError`` for the arguments, ``RuntimeError`` for what the drawing refuses. Gives the objects of the drawing the
        call gave back."""
        editor = self._editor if editor is None else editor
        given: Any = _net.Array[_dxf.DxfEntity](entities)
        try:
            if how == "offset":
                if not entities:
                    return []
                return list(editor.Offset(entities[0], args[0], args[1]))
            if how == "move":
                return list(editor.Move[_dxf.DxfEntity](given, args[0]))
            if how == "copy":
                return list(editor.Copy[_dxf.DxfEntity](given, args[0]))
            if how == "rotate":
                return list(editor.Rotate[_dxf.DxfEntity](given, args[0], args[1]))
            if how == "scale":
                return list(editor.Scale[_dxf.DxfEntity](given, args[0], args[1]))
            return list(editor.Mirror[_dxf.DxfEntity](given, args[0], args[1], args[2]))
        except _net.ArgumentException as error:
            raise _refused(error) from None
        except _net.InvalidOperationException as error:
            raise RuntimeError(str(error.Message)) from None

    def _insert_group(self, scope: Any, batch: Any, space: Any, group: list[Any], inserted: list[str | None]) -> None:
        # A layer the drawing lacks is made by the copy; the layer box and the Layer manager are told.
        layers = self.document.Database.LayerTable
        missing = sorted(
            {str(e.Layer) for e in group if str(e.Layer) and not layers.Has(str(e.Layer))},
            key=lambda n: (n.upper(), n),
        )
        copies = batch.WblockCloneObjects(_net.Array[_dxf.DxfEntity](group), space.Block)
        for copy in copies:
            # An MTEXT copied into a drawing of R12 is written as TEXTs and is in no space itself.
            if not copy.Handle or copy.IsErased:
                inserted.append(None)
                continue
            # A dimension made in code comes with a picture that is empty: the app draws it, as DIM would.
            if isinstance(copy, _dxf.DxfDimension) and not any(not e.IsErased for e in copy.DimBlock):
                _cad.CadDimensionPicture.Redraw(copy)
            scope.Added(space, copy)
            inserted.append(str(copy.Handle))
        for layer in missing:
            scope.Record(_cad.CadEditRecord(_cad.CadEditRecordKind.TableChanged, None, "Layer", None, layer))

    def _replace(self, old: Any, handle: str, entity: Entity) -> None:
        """Writes a changed copy over the object of the drawing, which keeps its handle (AutoCAD's ``HandOverTo``). A
        dimension keeps the block it had and is drawn again in it (``CadDimensionPicture.Redraw``)."""
        space = self.document.FindSpace(handle)
        made = entity._source.WblockCloneObjects(  # pyright: ignore[reportPrivateUsage]
            _net.Array[_dxf.DxfEntity]([entity.net]), space.Block
        )[0]
        fresh = made.Clone()
        made.Erase()
        spare: Any = None
        if isinstance(fresh, _dxf.DxfDimension) and isinstance(old, _dxf.DxfDimension) and old.DimBlock is not None:
            spare = fresh.DimBlock
            fresh.DimBlock = old.DimBlock
        old.HandOverTo(fresh, True, True)
        if spare is not None and spare.IsAnonymous:
            spare.Erase()
        drawn = self.document.FindEntity(handle)
        if isinstance(drawn, _dxf.DxfDimension):
            _cad.CadDimensionPicture.Redraw(drawn)

    # ---- layers -----------------------------------------------------------------------------------------------------

    def layers(self) -> list[Look]:
        looks: list[Look] = []
        for layer in self.document.Database.LayerTable:
            true_color = layer.TrueColor
            looks.append(
                Look(
                    name=str(layer.Name),
                    color=int(layer.ColorIndex),
                    true_color=None if true_color is None else int(true_color),
                    linetype=str(layer.LinetypeName),
                    line_weight=int(layer.LineWeight),
                    description=str(layer.Description),
                    is_on=not layer.IsOff,
                    is_frozen=bool(layer.IsFrozen),
                    is_locked=bool(layer.IsLocked),
                    is_plottable=bool(layer.IsPlottable),
                )
            )
        return looks

    def _write_layer(self, scope: Any, look: Look, add: bool) -> None:
        layers = self.document.Database.LayerTable
        if not self.document.Database.LinetypeTable.Has(look.linetype):
            raise RuntimeError(f"The linetype {look.linetype} is not in the drawing {self.name}.")
        if add:
            if layers.Has(look.name):
                raise RuntimeError(f'Layer "{look.name}" already exists.')
            layer = _dxf.DxfLayerTableRecord()
            layer.Name = look.name
        else:
            if not layers.Has(look.name):
                raise RuntimeError(f'The drawing {self.name} has no layer "{look.name}".')
            layer = layers[look.name]
        layer.Color = look_color(look)
        layer.LinetypeName = look.linetype
        layer.LineWeight = _dxf.DxfLineWeight(look.line_weight)
        layer.IsOff = not look.is_on
        layer.IsFrozen = look.is_frozen
        layer.IsLocked = look.is_locked
        layer.IsPlottable = look.is_plottable
        if add:
            layers.Add(layer)
            scope.Record(_cad.CadEditRecord(_cad.CadEditRecordKind.TableChanged, None, "New layer", None, look.name))
        else:
            # A layer changed is told to the document, which draws the spaces again: its objects may have gone off or frozen.
            scope.Record(_cad.CadEditRecord(_cad.CadEditRecordKind.TableChanged, None, "Layer", None, look.name))
        layer.Description = look.description

    @property
    def current_dim_style(self) -> str:
        return str(_cad.CadDimStyleEditor.Current(self.document.Database))

    @current_dim_style.setter
    def current_dim_style(self, value: str) -> None:
        result = _cad.CadDimStyleEditor.SetCurrent(self.document, value)
        if not result.Succeeded:
            raise RuntimeError(str(result.Message))

    @property
    def current_layer(self) -> str:
        return str(self.document.Database.Clayer)

    @current_layer.setter
    def current_layer(self, value: str) -> None:
        result = _cad.CadLayerEditor.SetCurrent(self.document, value)
        if not result.Succeeded:
            raise RuntimeError(str(result.Message))

    # ---- layouts, styles, blocks --------------------------------------------------------------------------------------

    @property
    def layouts(self) -> list[str]:
        return [str(space.Name) for space in self._spaces(None)]

    @property
    def current_layout(self) -> str:
        space = self.document.CurrentSpace
        return str((space if space is not None else self.document.ModelSpace).Name)

    @current_layout.setter
    def current_layout(self, value: str) -> None:
        space = self._space_named(value)
        result = self.document.ShowSpace(space)
        if not result.Succeeded:
            raise RuntimeError(str(result.Message))

    def _scratch(self) -> Any:
        """A copy of the whole drawing, from which the records of a table are taken as copies of their own."""
        return _dxf.DxfDatabase.Parse(self.document.Database.ToText())

    @property
    def text_styles(self) -> list[Any]:
        return sorted(list(self._scratch().TextStyleTable), key=_name_key)

    @property
    def dimension_styles(self) -> list[Any]:
        return sorted(list(self._scratch().DimStyleTable), key=_name_key)

    @property
    def blocks(self) -> list[Any]:
        blocks = [
            block
            for block in self._scratch().BlockTable
            if not block.IsLayout and not block.IsAnonymous and not str(block.Name).startswith("*")
        ]
        # The blocks the loaded external references bring (grid|TAG), which the drawing's own table holds none of; one it
        # holds a record of is kept as the record has it.
        have = {str(block.Name).upper() for block in blocks}
        blocks.extend(block for block in self.document.Xrefs.GetDependentBlocks() if str(block.Name).upper() not in have)
        return sorted(blocks, key=_name_key)

    # ---- the user ---------------------------------------------------------------------------------------------------

    def pick_point(self, message: str, base: Any) -> Any:
        raise _inside_only("pick_point")

    def pick_object(self, message: str) -> tuple[str, Any, Any] | None:
        raise _inside_only("pick_object")

    def pick_objects(self, message: str) -> list[tuple[str, Any, Any]]:
        raise _inside_only("pick_objects")

    # ---- the file ---------------------------------------------------------------------------------------------------

    def _saving(self, action: Callable[[], object]) -> None:
        """Runs a save. A drawing cannot be saved while a group of edits is open: the document ends the group for the save and
        opens it again, and what is done after the save is one undo step with what was done before it."""
        self.document.SavingInGroup(_net.Action(action))

    def save(self) -> None:
        self._saving(self.document.Save)

    def save_as(self, path: str, version: str | None) -> None:
        if version is None:
            self._saving(lambda: self.document.SaveAs(path))
        else:
            self._saving(lambda: self.document.SaveAs(path, version, None))

    def close(self, discard: bool) -> None:
        self._closed = self.name

    # ---- the view, the selection by area, the variables -------------------------------------------------------------

    @property
    def _editor(self) -> Any:
        """The ``CadEditor`` of a command session the drawing makes once, as the app makes one for a drawing it shows."""
        if self._session is None:
            self._session = _cad.CadCommandSession(self.document)
        return self._session.Editor

    @property
    def has_window(self) -> bool:
        return False

    def zoom_extents(self) -> None:
        raise _no_view("zoom_extents")

    def zoom_window(self, first: Any, second: Any) -> None:
        raise _no_view("zoom_window")

    def zoom_objects(self, handles: list[str]) -> None:
        raise _no_view("zoom_objects")

    def zoom_center(self, center: Any, height: float) -> None:
        raise _no_view("zoom_center")

    def zoom_scaled(self, factor: float) -> None:
        raise _no_view("zoom_scaled")

    def zoom_previous(self) -> bool:
        raise _no_view("zoom_previous")

    def pan(self, displacement: Any) -> None:
        raise _no_view("pan")

    def regen(self) -> None:
        self._editor.Regen()

    @property
    def view(self) -> Box:
        raise _no_view("view")

    @view.setter
    def view(self, box: Box) -> None:
        raise _no_view("view")

    @property
    def named_views(self) -> list[str]:
        raise _no_view("named_views")

    def save_view(self, name: str) -> None:
        raise _no_view("save_view")

    def restore_view(self, name: str) -> None:
        raise _no_view("restore_view")

    def delete_view(self, name: str) -> None:
        raise _no_view("delete_view")

    def get_variable(self, name: str) -> Any:
        try:
            return self._editor.GetVariable(name)
        except _net.ArgumentException as error:
            raise _refused(error) from None

    def set_variable(self, name: str, value: Any) -> None:
        try:
            self._editor.SetVariable(name, value)
        except _net.ArgumentException as error:
            raise _refused(error) from None

    # ---- rendering --------------------------------------------------------------------------------------------------

    def render(self, kind: str, path: str | None, options: RenderOptions) -> Any:
        """Draws the drawing with no window through ``CadRender``: ``png``, ``svg`` and ``pdf`` write ``path`` and give nothing;
        ``image`` gives the ``CadRenderImage``, ``transform`` the nine numbers of the matrix and ``masks`` and ``to_coco`` the
        ``CadRenderLabels``. A refusal of the options is a ``ValueError``."""
        facade = _render.load()
        document = self.document
        if kind == "pdf":
            try:
                facade.CadRender.WritePdf(path, document, options.layout)
            except _net.ArgumentException as error:
                raise _refused(error) from None
            return None
        shown = facade.CadRenderOptions()
        shown.Layout = options.layout
        if options.box is not None:
            shown.Box = _cad.DisplayBox(options.box.min_x, options.box.min_y, options.box.max_x, options.box.max_y)
        shown.Width = options.width or 0
        shown.Height = options.height or 0
        shown.Dpi = options.dpi
        shown.Background = options.background
        try:
            if kind == "transform":
                return facade.CadRender.Transform(document, shown)
            if kind in ("masks", "to_coco"):
                classes = None if options.classes is None else _net.Array[_net.Int32](list(options.classes))
                return facade.CadRender.DrawLabels(document, shown, classes)
            if kind == "image":
                return facade.CadRender.Draw(document, shown)
            (facade.CadRender.WritePng if kind == "png" else facade.CadRender.WriteSvg)(path, document, shown)
        except _net.ArgumentException as error:
            raise _refused(error) from None
        return None

    def _taken(self, handles: Any) -> list[tuple[str, Any, Any]]:
        entities = [self.document.FindEntity(handle) for handle in handles]
        return self._pack([entity for entity in entities if entity is not None and not entity.IsErased])

    def select_all(self) -> list[tuple[str, Any, Any]]:
        return self._taken(self._editor.SelectAll())

    def select_window(self, first: Any, second: Any) -> list[tuple[str, Any, Any]]:
        try:
            return self._taken(self._editor.SelectWindow(first, second))
        except _net.ArgumentException as error:
            raise _refused(error) from None

    def select_crossing(self, first: Any, second: Any) -> list[tuple[str, Any, Any]]:
        try:
            return self._taken(self._editor.SelectCrossingWindow(first, second))
        except _net.ArgumentException as error:
            raise _refused(error) from None

    # ---- where things are drawn (PW6.2) -----------------------------------------------------------------------------

    def _space(self, layout: str | None) -> Any:
        """The space of a layout name (model space where None); ValueError for a name the drawing lacks."""
        return self.document.ModelSpace if layout is None else self._spaces(layout)[0]

    @property
    def _as_drawn(self) -> Any:
        """The document set up as the app sets one up on opening it (``CadRender.Prepare``: the fonts and measure of the canvas),
        for a call that reads the boxes of words. Without it a drawing of a file is laid out with every letter 0.6 of its height
        wide until the first picture of the process sets the canvas's measure for every drawing, and the boxes change with it."""
        document = self.document
        _render.load().CadRender.Prepare(document)
        return document

    def bounds(self, layout: str | None, handles: list[str] | None) -> tuple[list[str], Any]:
        if handles is not None:
            table = _cad.CadSpatial.Bounds(self._as_drawn, _net.Array[_net.String](handles))
        else:
            table = _cad.CadSpatial.Bounds(self._as_drawn, self._space(layout))
        return [str(handle) for handle in table.Handles], table.Boxes

    def overlaps(
        self,
        layout: str | None,
        words: list[str] | None,
        over: list[str] | None,
        handles: list[str] | None,
        tolerance: float,
    ) -> tuple[list[str], list[str], Any]:
        options = _cad.CadOverlapOptions()
        if words is not None:
            options.Words = _net.Array[_net.String](words)
        if over is not None:
            options.Over = _net.Array[_net.String](over)
        if handles is not None:
            options.Handles = _net.Array[_net.String](handles)
        table = _cad.CadSpatial.Overlaps(
            self._as_drawn, self._space(layout), options, _geometry.Tolerance(tolerance, 1e-5)
        )
        return [str(w) for w in table.Words], [str(o) for o in table.Others], table.Boxes

    # ---- a drawing against a company standard (PW7.2) --------------------------------------------------------------

    def check(
        self, standard: str, model_scale: float, tolerance: float
    ) -> Any:
        """The scores and violations of the core's check of the drawing against the standard (its JSON text)."""
        try:
            result = _cad.CadStandards.Check(
                self._as_drawn, _cad.CadStandard.Parse(standard), model_scale, _geometry.Tolerance(tolerance, 1e-5)
            )
        except _net.ArgumentException as error:
            raise _refused(error) from None
        return result

    # ---- the export for AI pipelines (PW6.3) ------------------------------------------------------------------------

    def export(
        self, what: str, layout: str | None, tolerance: float, max_edges: int | None = None
    ) -> tuple[str, Any]:
        """The JSON text of the drawing (``what`` is ``"json"``) or of its graph (``"graph"``, with the ``CadDrawingGraph``
        too, whose edge array is copied in one piece); the core does the work."""
        try:
            if what == "graph":
                if max_edges is None:
                    graph = _cad.CadDrawingGraph.Build(self.document, layout, tolerance)
                else:
                    graph = _cad.CadDrawingGraph.Build(self.document, layout, tolerance, max_edges)
                return str(graph.ToJson()), graph
            return str(_cad.CadDrawingJson.Write(self.document, layout)), None
        except _net.ArgumentException as error:
            raise _refused(error) from None

    # ---- measuring before placing (PW7.1) -----------------------------------------------------------------------------

    @staticmethod
    def _measurement(measured: Any) -> tuple[tuple[float, ...] | None, list[tuple[float, ...]]]:
        box = measured.Box
        frames = [
            (float(f.X), float(f.Y), float(f.Width), float(f.Height), float(f.Rotation)) for f in measured.Frames
        ]
        return (
            None if box.IsEmpty else (float(box.MinX), float(box.MinY), float(box.MaxX), float(box.MaxY)),
            frames,
        )

    def measure(self, entity: Any) -> tuple[tuple[float, ...] | None, list[tuple[float, ...]]]:
        try:
            return self._measurement(_cad.CadMeasure.Of(self._as_drawn, entity))
        except _net.ArgumentException as error:
            raise _refused(error) from None

    def measure_leader(
        self,
        style: str | None,
        arrow: tuple[float, float],
        landing: tuple[float, float],
        words: str,
        scale: float | None,
    ) -> tuple[tuple[float, ...] | None, list[tuple[float, ...]]]:
        try:
            return self._measurement(
                _cad.CadMeasure.Leader(
                    self._as_drawn,
                    style,
                    _geo.GeoPoint2(arrow[0], arrow[1]),
                    _geo.GeoPoint2(landing[0], landing[1]),
                    words,
                    None if scale is None else _net.Nullable[_net.Double](scale),
                )
            )
        except _net.ArgumentException as error:
            raise _refused(error) from None

    @staticmethod
    def _space_options(kinds: list[str] | None, margin: float) -> Any:
        options = _cad.CadFreeSpaceOptions()
        if kinds is not None:
            options.Kinds = _net.Array[_net.String](kinds)
        options.Margin = margin
        return options

    def free_space(
        self,
        layout: str | None,
        frame: tuple[float, float, float, float],
        kinds: list[str] | None,
        margin: float,
        min_width: float,
        min_height: float,
        tolerance: float,
    ) -> list[tuple[float, float, float, float]]:
        options = self._space_options(kinds, margin)
        options.MinWidth = min_width
        options.MinHeight = min_height
        try:
            table = _cad.CadSpatial.FreeSpace(
                self._as_drawn,
                self._space(layout),
                _cad.DisplayBox(frame[0], frame[1], frame[2], frame[3]),
                options,
                _geometry.Tolerance(tolerance, 1e-5),
            )
        except (_net.ArgumentException, _net.InvalidOperationException) as error:
            raise _refused(error) from None
        numbers = [float(n) for n in table.Boxes]
        return [(numbers[i], numbers[i + 1], numbers[i + 2], numbers[i + 3]) for i in range(0, len(numbers), 4)]

    def fits(
        self,
        layout: str | None,
        candidate: tuple[float, float, float, float, float],
        kinds: list[str] | None,
        margin: float,
        tolerance: float,
    ) -> bool:
        try:
            return bool(
                _cad.CadSpatial.Fits(
                    self._as_drawn,
                    self._space(layout),
                    _cad.CadFrame(*candidate),
                    self._space_options(kinds, margin),
                    _geometry.Tolerance(tolerance, 1e-5),
                )
            )
        except _net.ArgumentException as error:
            raise _refused(error) from None


def active_drawing() -> Drawing:
    """The drawing the command of the tool was typed in (not the one shown later): ``active_drawing().add_line((0, 0), (10, 0))``.

    Example:
        drawing = active_drawing()            # the drawing the command was typed in
        drawing.add_line((0, 0), (10, 0))
        drawing.commit()                      # ['2B'] in a drawing whose last handle was 2A: one undo step
    """
    if not _tool.in_tool():
        raise _inside_only("active_drawing")
    return _tool.active_drawing()


def documents() -> Documents:
    """The drawings of the app, in the order it lists them.

    Example:
        [d.name for d in documents()]         # ['plan.dxf', 'Drawing2.dxf'] in the order the app lists them
    """
    if not _tool.in_tool():
        raise _inside_only("documents")
    return _tool.documents()


def open_drawing(path: str) -> Drawing:
    """Open a drawing file in the app, as OPEN does, and return it (the open one where it already is).

    Example:
        drawing = open_drawing(r"C:\\plans\\plan.dxf")   # shown in the app; open_drawing again gives the same one
    """
    if not _tool.in_tool():
        raise _inside_only("open_drawing")
    return _tool.open_drawing(os.path.abspath(path))


def new_drawing(template: str | None = None) -> Drawing:
    """Make a new drawing in the app, from a template file where one is given, and return it.

    Example:
        drawing = new_drawing()               # a blank drawing, shown; drawing.name == "Drawing1.dxf" in a fresh app
    """
    if not _tool.in_tool():
        raise _inside_only("new_drawing")
    return _tool.new_drawing(None if template is None else os.path.abspath(template))


def open_file(path: str) -> Drawing:
    """Open a DXF or DWG file with no app: ``open_file("plan.dxf")``. Changes reach the file after ``save()``.

    Example:
        drawing = open_file("plan.dxf")       # drawing.name == "plan.dxf", drawing.is_modified == False
        len(drawing.entities())               # 5 for the drawing built under Drawing.save_as
    """
    return Drawing(_LocalBackend(_cad.CadDocument.Open(os.path.abspath(path))))


def new_file() -> Drawing:
    """Make a blank drawing (AutoCAD 2013, layer 0 only) with no app and no file: ``new_file().save_as("new.dxf")``. Its
    Layout1 is a sheet of ISO A3 on its side (420 by 297 mm) in millimetres, as the app's NEW makes it, so a length on the
    layout is a length in millimetres with nothing set up.

    Example:
        drawing = new_file()                  # drawing.name == 'Drawing', drawing.layers has the layer 0 only
        drawing.add_line((0, 0), (100, 0))
        drawing.commit()                      # ['101']
        drawing.save_as("new.dxf")
    """
    database = _dxf.DxfDatabase("AC1027")
    _cad.CadTemplate.UseMillimetreSheet(database)
    return Drawing(_LocalBackend(_cad.CadDocument.From(database)))
