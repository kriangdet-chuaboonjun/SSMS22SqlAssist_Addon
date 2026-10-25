using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlAssist.Core.Results;

namespace SqlAssist.Results
{
    /// <summary>The cells selected in a results grid, read out of SSMS.</summary>
    internal sealed class GridSelection
    {
        public IReadOnlyList<ResultColumn> Columns { get; set; }
        /// <summary>Data-column indexes in grid order.</summary>
        public IReadOnlyList<int> Selected { get; set; }
        public IReadOnlyList<long> Rows { get; set; }
        /// <summary>Reads one row's values for the given data columns (other entries stay null).</summary>
        public Func<long, IReadOnlyList<int>, object[]> ReadRow { get; set; }
    }

    /// <summary>Finds the results grid the user is working in and reads its selection. SSMS has no API for this: the
    /// grid is Microsoft.SqlServer.GridControl's WinForms GridControl and its storage is SQLEditors' QEResultSet, so
    /// everything goes by reflection and each step tolerates members disappearing in later SSMS builds.
    /// Storage column 0 is the row-number column; data column i is storage column i + 1.</summary>
    internal static class ResultsGridReader
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string GridControlType = "Microsoft.SqlServer.Management.UI.Grid.GridControl";
        private static WeakReference<Control> _lastGrid;

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        /// <summary>Remembers the focused grid, if any; called while the grid's context menu is being built, since
        /// focus may have moved by the time the command runs.</summary>
        public static void Capture()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var grid = FocusedGrid();
            if (grid != null) _lastGrid = new WeakReference<Control>(grid);
        }

        /// <summary>True when keyboard focus is in a results grid.</summary>
        public static bool IsGridFocused() => FocusedGrid() != null;

        /// <summary>Null with <paramref name="problem"/> set when there is no grid or nothing to read.</summary>
        public static GridSelection TryRead(out string problem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            problem = null;
            try
            {
                var grid = FocusedGrid() ?? LastGrid() ?? ActiveDocumentGrid();
                if (grid == null) { problem = "Select cells in a results grid first."; return null; }
                object storage = Get(grid, "GridStorage");
                if (storage == null) { problem = "The results grid has no data."; return null; }

                var columns = ReadColumns(storage);
                if (columns.Count == 0) { problem = "The results grid has no columns."; return null; }

                var (rows, uiColumns) = ReadSelection(grid);
                var selected = new List<int>();
                foreach (int ui in uiColumns)
                {
                    int data = StorageColumn(grid, ui) - 1;
                    if (data >= 0 && data < columns.Count && !selected.Contains(data)) selected.Add(data);
                }
                if (selected.Count == 0 || rows.Count == 0) { problem = "Select one or more data cells in the results grid."; return null; }

                return new GridSelection
                {
                    Columns = columns,
                    Selected = selected,
                    Rows = rows,
                    ReadRow = (row, needed) => ReadRow(storage, columns.Count, row, needed),
                };
            }
            catch (Exception ex)
            {
                Log.Error("Read results grid", ex);
                problem = "Could not read the results grid: " + ex.Message;
                return null;
            }
        }

        private static Control FocusedGrid()
        {
            try
            {
                for (var c = Control.FromChildHandle(GetFocus()); c != null; c = c.Parent)
                    if (IsGrid(c)) return c;
            }
            catch (Exception ex)
            {
                Log.Error("Find focused grid", ex);
            }
            return null;
        }

        private static Control LastGrid() =>
            _lastGrid != null && _lastGrid.TryGetTarget(out var grid) && !grid.IsDisposed && grid.Visible ? grid : null;

        /// <summary>The active query window's grid that last had focus: SqlScriptEditorControl → m_sqlResultsControl
        /// (DisplaySQLResultsControl) → m_gridResultsPage (GridResultsTabPage) → LastFocusedControl.</summary>
        private static Control ActiveDocumentGrid()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(Package.GetGlobalService(typeof(SVsShellMonitorSelection)) is IVsMonitorSelection selection)) return null;
            selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out object frameObject);
            if (!(frameObject is IVsWindowFrame frame) || frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out object view) != 0) return null;
            object page = Field(Field(view, "m_sqlResultsControl"), "m_gridResultsPage");
            return Get(page, "LastFocusedControl") is Control c && IsGrid(c) ? c : null;
        }

        private static bool IsGrid(Control control)
        {
            for (var t = control.GetType(); t != null; t = t.BaseType)
                if (t.FullName == GridControlType) return true;
            return false;
        }

        private static List<ResultColumn> ReadColumns(object storage)
        {
            var columns = new List<ResultColumn>();
            int count = Get(storage, "NumberOfDataColumns") is int n ? n : 0;
            var names = Get(storage, "ColumnNames") as IList;
            for (int i = 0; i < count; i++)
            {
                string name = names != null && i < names.Count ? names[i] as string : null;
                string type = Call(storage, "GetFormattedDataTypeName", i) as string;
                var schema = Call(storage, "GetSchemaRow", i) as DataRow;
                columns.Add(new ResultColumn(name, type,
                    SchemaValue(schema, "BaseSchemaName") as string,
                    SchemaValue(schema, "BaseTableName") as string));
            }
            return columns;
        }

        /// <summary>Selected rows (ascending) and UI column indexes (ascending) across every selected block; with no
        /// selection, the current cell.</summary>
        private static (List<long> rows, List<int> columns) ReadSelection(Control grid)
        {
            var rows = new SortedSet<long>();
            var columns = new SortedSet<int>();
            if (Get(grid, "SelectedCells") is IEnumerable blocks)
                foreach (var block in blocks)
                {
                    if (!(Get(block, "X") is int x) || !(Get(block, "Right") is int right) ||
                        !(Get(block, "Y") is long y) || !(Get(block, "Bottom") is long bottom)) continue;
                    for (long r = y; r <= bottom && r >= 0; r++) rows.Add(r);
                    for (int c = x; c <= right && c >= 0; c++) columns.Add(c);
                }
            if (rows.Count == 0)
            {
                var args = new object[] { 0L, 0 };
                var current = grid.GetType().GetMethod("GetCurrentCell", Any);
                var parameters = current?.GetParameters();
                // Only (out long row, out int column): any other shape would leave args at (0, 0), i.e. the first cell.
                if (parameters != null && parameters.Length == 2 &&
                    parameters[0].ParameterType == typeof(long).MakeByRefType() && parameters[1].ParameterType == typeof(int).MakeByRefType())
                {
                    current.Invoke(grid, args);
                    if (args[0] is long row && args[1] is int column && row >= 0 && column >= 0) { rows.Add(row); columns.Add(column); }
                }
            }
            return (rows.ToList(), columns.ToList());
        }

        /// <summary>UI → storage column (they differ once the user drags columns around).</summary>
        private static int StorageColumn(Control grid, int uiIndex) =>
            Call(grid, "GetStorageColumnIndexByUIIndex", uiIndex) is int storage ? storage : uiIndex;

        private static object[] ReadRow(object storage, int columnCount, long row, IReadOnlyList<int> needed)
        {
            var values = new object[columnCount];
            foreach (int i in needed)
            {
                // GetCellData gives typed values (DateTime, decimal, byte[]…); the display string is the fallback.
                object value = Call(storage, "GetCellData", row, i + 1);
                if (value == null && Call(storage, "IsCellDataNull", row, i + 1) is bool isNull && !isNull)
                    value = Call(storage, "GetCellDataAsString", row, i + 1);
                values[i] = value;
            }
            return values;
        }

        private static object SchemaValue(DataRow row, string column) =>
            row != null && row.Table.Columns.Contains(column) && !(row[column] is DBNull) ? row[column] : null;

        private static object Get(object target, string property)
        {
            if (target == null) return null;
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(property, Any | BindingFlags.DeclaredOnly);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(target);
            }
            return null;
        }

        private static object Field(object target, string name)
        {
            for (var t = target?.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, Any | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(target);
            }
            return null;
        }

        private static object Call(object target, string method, params object[] args)
        {
            var types = args.Select(a => a.GetType()).ToArray();
            var m = target.GetType().GetMethod(method, Any, null, types, null);
            return m?.Invoke(target, args);
        }
    }
}
