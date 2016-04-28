enum SidebarPositions {
    Left,
    Right,
    Bottom
}

class Sidebar {

    constructor(private sidebarElem: JQuery, private transitionTime = 400) {
        if (sidebarElem.is(".right")) {
            this.position = SidebarPositions.Right;
            this.bodyClass = "hasRightSidebar";
        } else if (sidebarElem.is(".left")) {
            this.bodyClass = "hasLeftSidebar";
            this.position = SidebarPositions.Left;
        } else if (sidebarElem.is(".bottom")) {
            this.bodyClass = "hasBottomSidebar";
            this.position = SidebarPositions.Bottom;
        }
    }

    private bodyClass: string;
    private position: SidebarPositions;
    private map = $("#map");
    private active = false;

    IsActiv() {
        return this.active;
    }

    Show() {
        if (!this.active) {
            this.map.addClass(this.bodyClass, this.transitionTime, () => mapViewModel.Map.invalidateSize(true));
            this.sidebarElem.addClass("active", this.transitionTime);
            this.active = true;
            if (this.position === SidebarPositions.Bottom) {
                $(".sidebar").not(".bottom").addClass("hasBottomSidebar", this.transitionTime);
            }
        }
    }

    Hide() {
        if (this.active) {
            this.map.removeClass(this.bodyClass, this.transitionTime,()=>mapViewModel.Map.invalidateSize(true));
            this.sidebarElem.removeClass("active", this.transitionTime);
            this.active = false;
            if (this.position === SidebarPositions.Bottom)
                $(".sidebar").not(".bottom").removeClass("hasBottomSidebar", this.transitionTime);
        }
    }

    //Add(elem: JQuery) {
    //    elem.data("sidebar", this);
    //    if (this.active) {
    //        elem.hide();
    //        elem.show("blind", {}, this.transitionTime);
    //        elem.appendTo(this.sidebarElem);
    //    } else {
    //        elem.show();
    //        elem.appendTo(this.sidebarElem);
    //        this.Show();
    //    }
    //}

    //Remove(elem: JQuery) {
    //    if (elem.data("sidebar") === this) {
    //        if (this.sidebarElem.children().length > 1) {
    //            elem.hide("blind", {}, this.transitionTime, () => elem.removeData("sidebar").remove());
    //        } else {
    //            this.Hide();
    //            elem.removeData("sidebar").remove();
    //        }
    //    }
    //}
}