class Pager<T extends ClientModel.Entity> {
    constructor(source: KnockoutObservable<T[]>, options: IPagerOptions<T>) {
        var _self = this;
        this.Source = source;
        this.Columns(options.Columns);
        for (let col of this.Columns()) {
            if (col.CurrentSortMode() !== SortModes.None && this.CurrentlySortedBy() !== col) {
                if (this.CurrentlySortedBy() !== undefined)
                    this.CurrentlySortedBy().CurrentSortMode(SortModes.None);
                this.CurrentlySortedBy(col);
            }
        }
        if (options.SpecialColumnActions !== undefined)
            for (let action of options.SpecialColumnActions) {
                this.SpecialColumnActions.push(action);
            }
        if (options.SpecialActions !== undefined)
            for (let action of options.SpecialActions) {
                this.SpecialActions.push(action);
            }
        this.IdPrefix(options.IdPrefix);
        if (options.PageSize !== undefined)
            this.PageSize(options.PageSize);
        if (options.PageSizes !== undefined)
            this.PageSizes(options.PageSizes);
        this.UseResponsiveTable(options.UseResponsiveTable || false);
        this.UseStripedTable(options.UseStripedTable || false);
        this.UseSmallColumnControls(options.UseSmallColumnControls || false);
        this.UseSmallControls(options.UseSmallControls || false);
        if (options.EditingHelper !== undefined) {
            this.HasDetailView(options.EditingHelper.HasDetailView);
            this.ShowEditDeleteControls(options.ShowEditDeleteControls || false);
            this.EditingHelper(options.EditingHelper);
        }
        if (options.ShowColumnSelector !== undefined)
            this.ShowColumnSelector(options.ShowColumnSelector);
        if (options.ShowPageSizeSelector !== undefined)
            this.ShowPageSizeSelector(options.ShowPageSizeSelector);
    }

    IdPrefix = ko.observable<string>();
    HasDetailView = ko.observable(false);
    ShowEditDeleteControls = ko.observable(false);
    EditingHelper = ko.observable<EditingHelper<T>>();
    UseResponsiveTable = ko.observable(false);
    UseStripedTable = ko.observable(false);
    UseSmallColumnControls = ko.observable(false);
    UseSmallControls = ko.observable(false);
    Source: KnockoutObservable<T[]>;
    ActivePage = ko.observable(0);
    ShowPageSizeSelector = ko.observable(true);
    ShowColumnSelector = ko.observable(true);
    PageSize = ko.observable(10);
    PageSizes = ko.observableArray([5, 10, 15, 20, 30, 40, 50, 100]);
    PageCount = ko.computed({
        deferEvaluation: true,
        read: () => {
            let count = Math.ceil(this.FilteredSource().length / this.PageSize());
            if (this.ActivePage() * this.PageSize() >= count)
                this.ActivePage(count - 1);
            return count;
        },

    });
    Pages = ko.computed({
        deferEvaluation: true,
        read: () => {
            const out = [];
            for (let i = 0; i < this.PageCount(); i++) {
                out.push({ index: i + 1, selected: i === this.ActivePage() });
            }
            return out;
        }
    });
    HasPrevPage = ko.computed({
        deferEvaluation: true,
        read: () => this.ActivePage() !== 0
    });
    HasNextPage = ko.computed({
        deferEvaluation: true,
        read: () => this.ActivePage() !== this.PageCount() - 1
    });
    FilteredSource = ko.computed({
        deferEvaluation: true,
        read: () => this.Source().slice()
    });
    SortedSource = ko.computed({
        deferEvaluation: true,
        read: () => {
            if (this.CurrentlySortedBy() !== undefined)
                return this.FilteredSource().sort(this.CurrentlySortedBy().CurrentSortMode() === SortModes.Ascending ?
                    this.CurrentlySortedBy().Sorter :
                    (e1: T, e2: T) => this.CurrentlySortedBy().Sorter(e2, e1));
            else
                return this.FilteredSource();
        }
    });
    DisplaySource = ko.computed({
        deferEvaluation: true,
        read: () => {
            let start = this.PageSize() * this.ActivePage();
            return this.SortedSource().slice(start, start + this.PageSize());
        }
    });
    DisplayColumns = ko.computed({
        deferEvaluation: true,
        read: () => {
            return this.Columns().slice().filter((c) => c.Visible())
        },

    });
    DisplaySpecialActions = ko.computed({
        deferEvaluation: true,
        read: () => {
            return this.SpecialActions().slice().filter((c) => c.Visible());
        }

    });
    DisplaySpecialColumnActions = (data)=> {
        return this.SpecialColumnActions().slice().filter((c) => c.Visible.apply(data))
    };
    Columns = ko.observableArray<IPagerColumn<T>>();
    SpecialColumnActions = ko.observableArray<PagerSpecialColumnAction<T>>();
    CurrentlySortedBy = ko.observable<IPagerColumn<T>>();
    ShowSorters = ko.observable(true);
    SpecialActions = ko.observableArray<PagerSpecialAction>();

    SortBy = (column: IPagerColumn<T>) => {
        if (column.IsSortable())
            if (column.CurrentSortMode() === SortModes.Ascending)
                column.CurrentSortMode(SortModes.Descending);
            else if (column.CurrentSortMode() === SortModes.Descending)
                column.CurrentSortMode(SortModes.Ascending);
            else {
                column.CurrentSortMode(SortModes.Ascending);
                if (this.CurrentlySortedBy() !== undefined)
                    this.CurrentlySortedBy().CurrentSortMode(SortModes.None);
                this.CurrentlySortedBy(column);
            }
    }

    SelectPage = (page) => {
        this.ActivePage(page.index - 1);
    }
    SelectNextPage = () => {
        if (this.HasNextPage())
            this.ActivePage(this.ActivePage() + 1);
    }
    SelectPrevPage = () => {
        if (this.HasPrevPage())
            this.ActivePage(this.ActivePage() - 1);
    }
}

interface IPagerOptions<T extends ClientModel.Entity> {

    //Filters:(T)=>boolean|Array<(T)=>boolean>;
    Columns: Array<IPagerColumn<T>>;
    PageSize?: number;
    ShowPageSizeSelector?: boolean;
    ShowColumnSelector?: boolean;
    PageSizes?: number[];
    UseResponsiveTable?: boolean;
    UseStripedTable?: boolean;
    EditingHelper?: EditingHelper<T>;
    ShowEditDeleteControls?: boolean;
    IdPrefix: string;
    SpecialColumnActions?: PagerSpecialColumnAction<T>[];
    SpecialActions?: PagerSpecialAction[];
    UseSmallColumnControls?:boolean;
    UseSmallControls?:boolean;
}


class PagerSpecialColumnAction<T> {
    constructor(public Name: string, public Action: (e: T) => void, enabled?: ((v: T) => boolean) | KnockoutObservable<boolean>, visible?: KnockoutObservable<boolean> | ((v: T) => boolean)) {
        if (enabled !== undefined)
            if (ko.isObservable(enabled))
                this.Enabled = enabled;
            else
                this.Enabled = ko.computed(function () { return enabled(this); });
        if (visible !== undefined)
            if (ko.isObservable(visible))
                this.Visible = visible;
            else
                this.Visible = ko.computed(function () {
                    console.log(this);
                    return visible(this);
                });
    }

    Enabled: KnockoutObservable<boolean> = ko.computed(() => true);
    Visible: KnockoutObservable<boolean> = ko.computed(() => true);
}


class PagerSpecialAction {
    constructor(public Name: string, public Action: () => void, enabled?: (() => boolean) | KnockoutObservable<boolean>, visible?: KnockoutObservable<boolean> | (() => boolean)) {
        if (enabled !== undefined)
            if (ko.isObservable(enabled))
                this.Enabled = enabled;
            else
                this.Enabled = ko.computed(function () { return enabled(); });
        if (visible !== undefined)
            if (ko.isObservable(visible))
                this.Visible = visible;
            else
                this.Visible = ko.computed(function () { return visible(); });
    }

    Enabled: KnockoutObservable<boolean> = ko.computed(() => true);
    Visible: KnockoutObservable<boolean> = ko.computed(() => true);
}

interface IPagerFilter<T> {

}

enum SortModes {
    None,
    Ascending,
    Descending
}



interface IPagerColumn<T> {
    DisplayValue: (v: T) => KnockoutComputed<string>;
    IsSortable: () => boolean;
    CurrentSortMode: KnockoutObservable<SortModes>;
    Sorter: (e1: T, e2: T) => number;
    DisplayName: KnockoutComputed<string>;
    Name: string | KnockoutObservable<string>;
    Visible: KnockoutObservable<boolean>;
    CssClasses: string;
    Styles: string;
}

interface IPagerColumnOptions<T> {
    CssClasses?: string | string[];
    Styles?: string | { key: string, value: string }[]
    Width?: number;
    Sorter?: (e1: T, e2: T) => number;
    Renderer?: (v: T) => string;
    Visible?: boolean;
    SortMode?: SortModes;
}

class PagerColumn<T, VT> implements IPagerColumn<T> {
    Renderer: (v: VT) => string;
    ValueSorter: (e1: VT, e2: VT) => number;
    CssClasses = "";
    Styles = "";
    Visible = ko.observable(true);

    constructor(public Name: string | KnockoutObservable<string>, public Value: (v: T) => VT | KnockoutObservable<VT>, options: IPagerColumnOptions<VT> = {}) {
        if (options.Visible !== undefined)
            this.Visible(options.Visible);
        this.Renderer = options.Renderer || PagerColumn.DefaultRenderer();
        this.ValueSorter = options.Sorter;
        if (options.SortMode !== undefined)
            this.CurrentSortMode(options.SortMode)

        if (options.CssClasses !== undefined)
            if (options.CssClasses instanceof Array && options.CssClasses.length > 0) {
                this.CssClasses = options.CssClasses[0];
                for (let i = 1; i < options.CssClasses.length; i++) {
                    this.CssClasses + " " + options.CssClasses[i];
                }
            } else if (typeof options.CssClasses === "string")
                this.CssClasses = <string>options.CssClasses;
        if (options.Styles !== undefined)
            if (options.Styles instanceof Array && options.Styles.length > 0) {
                this.Styles = "";
                for (let style of <{ key: string, value: string }[]>options.Styles) {
                    this.Styles += style.key + ": " + style.value + ";";
                }

            } else if (typeof options.Styles === "string")
                this.Styles = <string>options.Styles;
        if (options.Width !== undefined)
            this.Styles = "width: " + options.Width + "px;" + (this.Styles || "");
    }

    IsSortable = () => this.ValueSorter !== undefined;
    CurrentSortMode = ko.observable(SortModes.None);
    Sorter = (e1: T, e2: T) => this.ValueSorter(ko.unwrap(this.Value(e1)), ko.unwrap(this.Value(e2)));
    DisplayValue = (v: T) => {
        return ko.computed<string>({
            deferEvaluation: true,
            read: () => {
                const value = ko.unwrap(this.Value(v));
                if (value === undefined)
                    return "";
                return this.Renderer(value);
            }
        }
        )
    };

    DisplayName = ko.computed<string>({
        deferEvaluation: true,
        read: () => this.CurrentSortMode() === SortModes.None ? ko.unwrap(this.Name) : ko.unwrap(this.Name) +
            (this.CurrentSortMode() === SortModes.Ascending ? " &#9650;" : " &#9660;"),

    })

    static StringSorter = () => (e1: string, e2: string) => e1 > e2 ? 1 : -1;
    static NumberSorter = () => (e1: number, e2: number) => e1 > e2 ? 1 : -1;
    static DateSorter = () => (e1: Date | number | string, e2: Date | number | string) => new Date(e1) - new Date(e2);
    static ArraySorter = () => (e1: any[], e2: any[]) => e1.length - e2.length;

    static DefaultRenderer = () => (v) => {
        if (v === undefined)
            return "";
        else if (v instanceof Date)
            return PagerColumn.DateRenderer()(v);
        else if (v instanceof Array) {
            return PagerColumn.ArrayRenderer()(v);
        }
        return v.toString();
    };
    static DateRenderer = () => (v: Date | number | string) => <string>moment(v).format("LL");
    static DateTimeRenderer = () => (v: Date | number | string) => <string>moment(v).format("LLL");
    static TimeRenderer = () => (v: Date | number | string) => <string>moment(v).format("LT");
    static ArrayRenderer<TS>(seperator = ", ", elementRenderer: (v: TS) => string = PagerColumn.DefaultRenderer()) {
        return (v: TS[]) => {
            let out = "";
            if (v !== undefined && v.length > 0) {
                out += elementRenderer(v[0]);
                for (let i = 1; i < v.length; i++)
                    out += seperator + elementRenderer(v[i]);
            }
            return out;
        }
    }

}
