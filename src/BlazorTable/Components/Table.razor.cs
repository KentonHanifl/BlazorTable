using BlazorDateRangePicker;
using LinqKit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.JSInterop;
using System.Text;

namespace BlazorTable
{
    public partial class Table<TableItem> : ITable<TableItem>
    {
        [Inject]
        public IJSRuntime JSRuntime { get; set; }

        private CancellationTokenSource SearchCancellation { get; set; }

        private const int DEFAULT_PAGE_SIZE = 10;

        [Parameter(CaptureUnmatchedValues = true)]
        public IReadOnlyDictionary<string, object> UnknownParameters { get; set; }

        /// <summary>
        /// Table CSS Class (Defaults to Bootstrap 4)
        /// </summary>
        [Parameter]
        public string TableClass { get; set; } = "table table-striped table-bordered table-hover table-sm";

        /// <summary>
        /// Table Head Class (Defaults to Bootstrap 4)
        /// </summary>
        [Parameter]
        public string TableHeadClass { get; set; } = "thead-light text-dark";

        /// <summary>
        /// Table Body Class
        /// </summary>
        [Parameter]
        public string TableBodyClass { get; set; } = "";

        /// <summary>
        /// Table Footer Class
        /// </summary>
        [Parameter]
        public string TableFooterClass { get; set; } = "text-white bg-secondary";

        /// <summary>
        /// Expression to set Row Class
        /// </summary>
        [Parameter]
        public Func<TableItem, string> TableRowClass { get; set; }

        /// <summary>
        /// Page Size, defaults to 15
        /// </summary>
        [Parameter]
        public int PageSize { get; set; } = DEFAULT_PAGE_SIZE;

        /// <summary>
        /// Allow Columns to be reordered
        /// </summary>
        [Parameter]
        public bool ColumnReorder { get; set; }

        [Parameter]
        public RenderFragment ChildContent { get; set; }

        /// <summary>
        /// IQueryable data source to display in the table
        /// </summary>
        [Parameter]
        public IQueryable<TableItem> ItemsQueryable { get; set; }

        /// <summary>
        /// Collection to display in the table
        /// </summary>
        [Parameter]
        public IEnumerable<TableItem> Items { get; set; }

        /// <summary>
        /// Search all columns for the specified string, supports spaces as a delimiter
        /// </summary>
        [Parameter]
        public string GlobalSearch { get; set; }

        /// <summary>
        /// An optional initial start date for the two column date filter.
        /// </summary>
        [Parameter]
        public DateTimeOffset? InitialTwoColumnDateFilterStart { get; set; }
        /// <summary>
        /// The start filter date of the two column date filter.
        /// </summary>
        public DateTimeOffset? TwoColumnDateFilterStart { get; set; }
        [Parameter]
        public EventCallback<DateTimeOffset?> InitialTwoColumnDateFilterStartChanged { get; set; }

        /// <summary>
        /// An optional initial end date for the two column date filter.
        /// </summary>
        [Parameter]
        public DateTimeOffset? InitialTwoColumnDateFilterEnd { get; set; }
        /// <summary>
        /// The end filter date of the two column date filter.
        /// </summary>
        public DateTimeOffset? TwoColumnDateFilterEnd { get; set; }
        [Parameter]
        public EventCallback<DateTimeOffset?> InitialTwoColumnDateFilterEndChanged { get; set; }

        [Parameter]
        public List<string> InitiallyHiddenColumnTitles { get; set; }

        [Parameter]
        public List<int> InitiallyHiddenColumnNumbers { get; set; }

        private void UpdateInitialDates(DateTimeOffset? start, DateTimeOffset? end)
        {
            InitialTwoColumnDateFilterStart = start;
            InitialTwoColumnDateFilterEnd = end;

            InitialTwoColumnDateFilterStartChanged.InvokeAsync(InitialTwoColumnDateFilterStart);
            InitialTwoColumnDateFilterEndChanged.InvokeAsync(InitialTwoColumnDateFilterEnd);
        }


        [Inject]
        private ILogger<ITable<TableItem>> Logger { get; set; }

        /// <summary>
        /// Collection of filtered items
        /// </summary>
        public IEnumerable<TableItem> FilteredItems { get; private set; }
        public IEnumerable<TableItem> NonPagedFilteredItems { get; private set; }

        /// <summary>
        /// List of All Available Columns
        /// </summary>
        public List<IColumn<TableItem>> Columns { get; } = new List<IColumn<TableItem>>();

        /// <summary>
        /// Current Page Number
        /// </summary>
        public int PageNumber { get; private set; }

        /// <summary>
        /// Total Count of Items
        /// </summary>
        public int TotalCount { get; private set; }

        /// <summary>
        /// Is Table in Edit mode
        /// </summary>
        public bool IsEditMode { get; private set; }

        /// <summary>
        /// Total Pages
        /// </summary>
        public int TotalPages => PageSize <= 0 ? 1 : (TotalCount + PageSize - 1) / PageSize;

        protected override void OnParametersSet()
        {
            TwoColumnDateFilterStart = InitialTwoColumnDateFilterStart;
            TwoColumnDateFilterEnd = InitialTwoColumnDateFilterEnd;
            Update();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                Update();
            }
            if (generatingCsv)
            {
                await JSRuntime.InvokeAsync<string>("GetCsvText", CsvHolderElement);
                generatingCsv = false;
                Refresh();
            }
        }

        private IEnumerable<TableItem> GetData(bool getAll = false)
        {
            if (Items != null || ItemsQueryable != null)
            {
                if (Items != null)
                {
                    ItemsQueryable = Items.AsQueryable();
                }

                foreach (var column in Columns)
                {
                    if (column.Filter != null)
                    {
                        ItemsQueryable = ItemsQueryable.Where(column.Filter.AddNullChecks());
                    }

                    if (column.IsStartDateColumn && TwoColumnDateFilterEnd != null)
                    {
                        var convertedEnd = DateTime.SpecifyKind(TwoColumnDateFilterEnd?.DateTime ?? throw new NullReferenceException("The end datetime should not be null."),
                                                                    DateTimeKind.Utc);
                        convertedEnd.AddDays(1).AddTicks(-1);
                        ItemsQueryable = ItemsQueryable.Where(
                            Expression.Lambda<Func<TableItem, bool>>(
                                Expression.LessThanOrEqual(
                                Expression.Convert(column.Field.Body, typeof(DateTime)),
                                Expression.Constant(convertedEnd)
                            ), column.Field.Parameters));
                    }

                    if (column.IsEndDateColumn && TwoColumnDateFilterStart != null)
                    {
                        var convertedStart = DateTime.SpecifyKind(TwoColumnDateFilterStart?.DateTime ?? throw new NullReferenceException("The start datetime should not be null."),
                                                                DateTimeKind.Utc);

                        ItemsQueryable = ItemsQueryable.Where(
                            Expression.Lambda<Func<TableItem, bool>>(
                                Expression.GreaterThanOrEqual(
                                Expression.Convert(column.Field.Body, typeof(DateTime)),
                                Expression.Constant(convertedStart)
                            ), column.Field.Parameters));
                    }
                }

                // Global Search
                if (!string.IsNullOrEmpty(GlobalSearch))
                {
                    ItemsQueryable = ItemsQueryable.Where(GlobalSearchQuery(GlobalSearch));
                }

                TotalCount = ItemsQueryable.Count();

                var sortColumn = Columns.Find(x => x.SortColumn);

                if (sortColumn != null)
                {
                    if (sortColumn.SortDescending)
                    {
                        ItemsQueryable = ItemsQueryable.OrderByDescending(sortColumn.Field);
                    }
                    else
                    {
                        ItemsQueryable = ItemsQueryable.OrderBy(sortColumn.Field);
                    }
                }

                // if the current page is filtered out, we should go back to a page that exists
                if (PageNumber > TotalPages)
                {
                    PageNumber = TotalPages - 1;
                }

                // if PageSize is zero, we return all rows and no paging
                if (PageSize <= 0 || getAll)
                    return ItemsQueryable.ToList();
                else
                    return ItemsQueryable.Skip(PageNumber * PageSize).Take(PageSize).ToList();
            }

            return Items;
        }

        private Dictionary<int, bool> detailsViewOpen = new Dictionary<int, bool>();

        /// <summary>
        /// Gets Data and redraws the Table
        /// </summary>
        public void Update()
        {
            FilteredItems = GetData();
            NonPagedFilteredItems = GetData(getAll: true);
            Refresh();
        }

        /// <summary>
        /// Adds a Column to the Table
        /// Only call when adding column initially -- Kenton
        /// </summary>
        /// <param name="column"></param>
        public void AddColumn(IColumn<TableItem> column)
        {
            column.Table = this;

            if (column.Type == null)
            {
                column.Type = column.Field?.GetPropertyMemberInfo().GetMemberUnderlyingType();
            }

            int nextIndex = Columns.Count - 1;

            //if index is in the initially hidden column numbers or the title is in the hidden columns, don't show the column
            if ((InitiallyHiddenColumnNumbers != null && InitiallyHiddenColumnNumbers.Contains(nextIndex)) ||
                (InitiallyHiddenColumnTitles != null && InitiallyHiddenColumnTitles.Contains(column.Title)))
                column.IsHidden = true;

            Columns.Add(column);

            Refresh();
        }

        public void ReaddColumn(IColumn<TableItem> column)
        {
            Columns.First(c => c == column).IsHidden = false;
            Refresh();
        }

        /// <summary>
        /// Removes a Column from the Table
        /// </summary>
        /// <param name="column"></param>
        public void RemoveColumn(IColumn<TableItem> column)
        {
            Columns.First(c => c.Title == column.Title).IsHidden = true;
            Refresh();
        }

        /// <summary>
        /// Removes a column at a specific index
        /// </summary>
        /// <param name="i"></param>
        public void RemoveColumn(int i)
        {
            Columns[i].IsHidden = true;
            Refresh();
        }

        /// <summary>
        /// Removes a column by the title string
        /// </summary>
        /// <param name="title"></param>
        public void RemoveColumn(string title)
        {
            var colToRemove = Columns.FirstOrDefault(c => c.Title == title);
            if (colToRemove != null)
            {
                colToRemove.IsHidden = true;
                Refresh();
            }
        }

        /// <summary>
        /// Go to First Page
        /// </summary>
        public void FirstPage()
        {
            if (PageNumber != 0)
            {
                PageNumber = 0;
                detailsViewOpen.Clear();
                Update();
            }
        }

        /// <summary>
        /// Go to Next Page
        /// </summary>
        public void NextPage()
        {
            if (PageNumber + 1 < TotalPages)
            {
                PageNumber++;
                detailsViewOpen.Clear();
                Update();
            }
        }

        /// <summary>
        /// Go to Previous Page
        /// </summary>
        public void PreviousPage()
        {
            if (PageNumber > 0)
            {
                PageNumber--;
                detailsViewOpen.Clear();
                Update();
            }
        }

        /// <summary>
        /// Go to Last Page
        /// </summary>
        public void LastPage()
        {
            PageNumber = TotalPages - 1;
            detailsViewOpen.Clear();
            Update();
        }

        /// <summary>
        /// Redraws the Table using EditTemplate instead of Template
        /// </summary>
        public void ToggleEditMode()
        {
            IsEditMode = !IsEditMode;
            StateHasChanged();
        }

        /// <summary>
        /// Redraws Table without Getting Data
        /// </summary>
        public void Refresh()
        {
            StateHasChanged();
        }

        /// <summary>
        /// Save currently dragged column
        /// </summary>
        private IColumn<TableItem> DragSource;

        /// <summary>
        /// Handles the Column Reorder Drag Start and set DragSource
        /// </summary>
        /// <param name="column"></param>
        private void HandleDragStart(IColumn<TableItem> column)
        {
            DragSource = column;
        }

        /// <summary>
        /// Handles Drag Drop and inserts DragSource column before itself
        /// </summary>
        /// <param name="column"></param>
        private void HandleDrop(IColumn<TableItem> column)
        {
            int index = Columns.FindIndex(a => a == column);

            Columns.Remove(DragSource);

            Columns.Insert(index, DragSource);

            StateHasChanged();
        }

        /// <summary>
        /// Return row class for item if expression is specified
        /// </summary>
        /// <param name="item">TableItem to return for</param>
        /// <returns></returns>
        private string RowClass(TableItem item)
        {
            return TableRowClass?.Invoke(item);
        }

        /// <summary>
        /// Set the template to use for empty data
        /// </summary>
        /// <param name="emptyDataTemplate"></param>
        public void SetEmptyDataTemplate(EmptyDataTemplate emptyDataTemplate)
        {
            _emptyDataTemplate = emptyDataTemplate?.ChildContent;
        }

        private RenderFragment _emptyDataTemplate;

        /// <summary>
        /// Set the template to use for loading data
        /// </summary>
        /// <param name="loadingDataTemplate"></param>
        public void SetLoadingDataTemplate(LoadingDataTemplate loadingDataTemplate)
        {
            _loadingDataTemplate = loadingDataTemplate?.ChildContent;
        }

        private RenderFragment _loadingDataTemplate;

        /// <summary>
        /// Set the template to use for detail
        /// </summary>
        /// <param name="detailTemplate"></param>
        public void SetDetailTemplate(DetailTemplate<TableItem> detailTemplate)
        {
            _detailTemplate = detailTemplate?.ChildContent;
        }

        private RenderFragment<TableItem> _detailTemplate;

        private SelectionType _selectionType;

        /// <summary>
        /// Select Type: None, Single or Multiple
        /// </summary>
        [Parameter]
        public SelectionType SelectionType
        {
            get { return _selectionType; }
            set
            {
                _selectionType = value;
                if (_selectionType == SelectionType.None)
                {
                    SelectedItems.Clear();
                }
                else if (_selectionType == SelectionType.Single && SelectedItems.Count > 1)
                {
                    SelectedItems.RemoveRange(1, SelectedItems.Count - 1);
                }
                StateHasChanged();
            }
        }

        /// <summary>
        /// Contains Selected Items
        /// </summary>
        [Parameter]
        public List<TableItem> SelectedItems { get; set; } = new List<TableItem>();

        /// <summary>
        /// Action performed when the row is clicked.
        /// </summary>
        [Parameter]
        public Action<TableItem> RowClickAction { get; set; }

        /// <summary>
        /// Handles the onclick action for table rows.
        /// This allows the RowClickAction to be optional.
        /// </summary>
        private void OnRowClickHandler(TableItem tableItem)
        {
            try
            {
                RowClickAction?.Invoke(tableItem);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "RowClickAction threw an exception: {0}", ex);
            }

            switch (SelectionType)
            {
                case SelectionType.None:
                    return;
                case SelectionType.Single:
                    SelectedItems.Clear();
                    SelectedItems.Add(tableItem);
                    break;
                case SelectionType.Multiple:
                    if (SelectedItems.Contains(tableItem))
                        SelectedItems.Remove(tableItem);
                    else
                        SelectedItems.Add(tableItem);
                    break;
            }
        }

        private Expression<Func<TableItem, bool>> GlobalSearchQuery(string value)
        {
            Expression<Func<TableItem, bool>> expression = null;

            foreach (string keyword in value.Trim().Split(" "))
            {
                Expression<Func<TableItem, bool>> tmp = null;

                foreach (var column in Columns.Where(x => x.Field != null))
                {
                    var newQuery = Expression.Lambda<Func<TableItem, bool>>(
                        Expression.AndAlso(
                            Expression.NotEqual(column.Field.Body, Expression.Constant(null)),
                            Expression.GreaterThanOrEqual(
                                Expression.Call(
                                    Expression.Call(column.Field.Body, "ToString", Type.EmptyTypes),
                                    typeof(string).GetMethod(nameof(string.IndexOf), new[] { typeof(string), typeof(StringComparison) }),
                                    new[] { Expression.Constant(keyword), Expression.Constant(StringComparison.OrdinalIgnoreCase) }),
                            Expression.Constant(0))),
                            column.Field.Parameters[0]);

                    if (tmp == null)
                        tmp = newQuery;
                    else
                        tmp = tmp.Or(newQuery);
                }

                if (expression == null)
                    expression = tmp;
                else
                    expression = expression.And(tmp);
            }

            return expression;
        }

        private async Task SearchWithDelay(ChangeEventArgs x)
        {
            //cancel previous search
            SearchCancellation?.Cancel();

            //make new cancellation token
            SearchCancellation = new CancellationTokenSource();
            var token = SearchCancellation.Token;

            //wait for Xms, then search if not cancelled
            //cannot await this task here. will not work with the cancellation token properly
            _ = Task.Run(async () =>
            {
                await Task.Delay(200).ConfigureAwait(false);
                if (!token.IsCancellationRequested)
                {
                    GlobalSearch = x.Value.ToString();
                    //following line threw an error if not awaited in this way
                    await InvokeAsync(Update).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Shows Search Bar above the table
        /// </summary>
        [Parameter]
        public bool ShowSearchBar { get; set; }

        /// <summary>
        /// Show or hide table footer. Hide by default.
        /// </summary>
        [Parameter]
        public bool ShowFooter { get; set; }

        /// <summary>
        /// Show the two column date filter
        /// </summary>
        [Parameter]
        public bool ShowTwoColumnDateFilter { get; set; }

        /// <summary>
        /// Show the two column date filter, with call back to get different data set
        /// </summary>
        [Parameter]
        public bool ShowActiveTwoColumnDateFilter { get; set; }

        /// <summary>
        /// Show the column selector (hides/shows columns)
        /// </summary>
        [Parameter]
        public bool ShowColumnSelector { get; set; }

        private bool IsShowColumnSelector { get; set; } = false;


        /// <summary>
        /// KENTON All of this is for generating a CSV
        /// </summary>

        private ElementReference CsvHolderElement;
        private bool generatingCsv = false;
        //has to wait for render to happen, so actual saving happens in onAfterRender
        private async Task SaveAsCsv()
        {
            generatingCsv = true;
            Refresh();
        }


        /// <summary>
        /// Set the template to use for date filter data
        /// </summary>
        /// <param name="dateFragment"></param>
        public void SetDateFragment(DateFragment dateFragment)
        {
            _dateFragment = dateFragment?.ChildContent;
        }

        private RenderFragment _dateFragment;




        /// <summary>
        /// Show the generic add button
        /// </summary>
        [Parameter]
        public bool ShowAdd { get; set; }

        /// <summary>
        /// Generic create button
        /// </summary>
        /// <param name="addFragment"></param>
        public void SetAddFragment(AddFragment addFragment)
        {
            _addFragment = addFragment?.ChildContent;
        }

        private RenderFragment _addFragment;



        /// <summary>
        /// Set Table Page Size
        /// </summary>
        /// <param name="pageSize"></param>
        public void SetPageSize(int pageSize)
        {
            PageSize = pageSize;
            Update();
        }

        Dictionary<string, DateRange> DateRanges => new Dictionary<string, DateRange> {
            { "Today", new DateRange
                {
                    Start = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day),
                    End = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day).AddDays(1).AddTicks(-1)
                }
            } ,
            { "Yesterday", new DateRange
                {
                    Start = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day).AddDays(-1),
                    End = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day).AddTicks(-1)
                }
            } ,
            { "Last 7 Days", new DateRange
                {
                    Start = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day).AddDays(-6),
                    End = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day).AddDays(1).AddTicks(-1)
                }
            } ,
            { "Last 30 Days", new DateRange
                {
                    Start = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day).AddDays(-29),
                    End = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day).AddDays(1).AddTicks(-1)
                }
            } ,
            { "This month", new DateRange
                {
                    Start = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1),
                    End = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(1).AddTicks(-1)
                }
            } ,
            { "Previous month" , new DateRange
                {
                    Start = new DateTime(DateTime.Now.AddMonths(-1).Year, DateTime.Now.AddMonths(-1).Month, 1),
                    End = new DateTime(DateTime.Now.AddMonths(-1).Year, DateTime.Now.AddMonths(-1).Month, 1).AddMonths(1).AddTicks(-1)
                }
            }
        };
    }
}
