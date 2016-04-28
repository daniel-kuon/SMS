
$("#harbourData")
    .on("mouseenter",
        "tr",
        function() {
            model.CalculateDistances(ko.contextFor(this).$data);
        });

$("#navPoints")
    .on("mouseenter",
        "tr",
        function() {
            ko.contextFor(this).$data.Show(true);
        });
$("#navPoints")
    .on("mouseleave",
        "tr",
        function() {
            ko.contextFor(this).$data.Hide();
        });
        