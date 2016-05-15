var SidebarPositions;
(function (SidebarPositions) {
    SidebarPositions[SidebarPositions["Left"] = 0] = "Left";
    SidebarPositions[SidebarPositions["Right"] = 1] = "Right";
    SidebarPositions[SidebarPositions["Bottom"] = 2] = "Bottom";
})(SidebarPositions || (SidebarPositions = {}));
var Sidebar = (function () {
    function Sidebar(sidebarElem, transitionTime) {
        if (transitionTime === void 0) { transitionTime = 400; }
        this.sidebarElem = sidebarElem;
        this.transitionTime = transitionTime;
        this.map = $("#map");
        this.active = false;
        if (sidebarElem.is(".right")) {
            this.position = SidebarPositions.Right;
            this.bodyClass = "hasRightSidebar";
        }
        else if (sidebarElem.is(".left")) {
            this.bodyClass = "hasLeftSidebar";
            this.position = SidebarPositions.Left;
        }
        else if (sidebarElem.is(".bottom")) {
            this.bodyClass = "hasBottomSidebar";
            this.position = SidebarPositions.Bottom;
        }
    }
    Sidebar.prototype.IsActiv = function () {
        return this.active;
    };
    Sidebar.prototype.Show = function () {
        if (!this.active) {
            this.map.addClass(this.bodyClass, this.transitionTime, function () { return mapViewModel.Map.invalidateSize(true); });
            this.sidebarElem.addClass("active", this.transitionTime);
            this.active = true;
            if (this.position === SidebarPositions.Bottom) {
                $(".sidebar").not(".bottom").addClass("hasBottomSidebar", this.transitionTime);
            }
        }
    };
    Sidebar.prototype.Hide = function () {
        if (this.active) {
            this.map.removeClass(this.bodyClass, this.transitionTime, function () { return mapViewModel.Map.invalidateSize(true); });
            this.sidebarElem.removeClass("active", this.transitionTime);
            this.active = false;
            if (this.position === SidebarPositions.Bottom)
                $(".sidebar").not(".bottom").removeClass("hasBottomSidebar", this.transitionTime);
        }
    };
    return Sidebar;
}());
