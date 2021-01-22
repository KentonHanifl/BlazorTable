//Is the hide function already bound to the body?
var HideFunctionIsBound = false;

//Meant to be bound to body to hide popover on the next click outside of the popover
//Unbinds itself from body after being called
function HidePopoverClickOutside(e) {
    //If the element clicked on does not have a parent that is a popover
    if ($(e.target).parents(".hide-next-click-popover").length < 1) {
        HideAllPopovers();
        //unbind this function from the body
        $("body").unbind("click", HidePopoverClickOutside);
        HideFunctionIsBound = false;
    }
}

function HideAllPopovers() {
    //just calls hide() on all custom popovers
    $(".hide-next-click-popover").each(function (index, elm) {
        $(elm).hide();
    });
}

/* JS INTEROPS */
//binds HidePopoverClickOutside to the click of the body if it is not already bound
function BindBodyHidePopover() {
    if (!HideFunctionIsBound) {
        $('body').bind('click', HidePopoverClickOutside);
        HideFunctionIsBound = true;
    }
}

//Returns a bool, true if there are any popovers shown, false if none.
function IsPopoversShown() {
    const pop = $(".hide-next-click-popover")[0];
    if (pop != null) {
        return $(pop).css("display") != "none";
    } 
    return false;
}