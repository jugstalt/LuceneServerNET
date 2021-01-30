$(document).ready(function () {
    var $resultsPanel = $('#content-search-results');

    $("#content-search")
        .keyup(function (e) {
            var term = $(this).val().trim();

            if (!term) {
                $resultsPanel.empty();
                return;
            }

            $.ajax({
                url: '/index',
                data: { handler:'search', term: term },
                success: function (result) {
                    $resultsPanel.empty();

                    for (var i in result.items)
                    {
                        var item = result.items[i];

                        var $resultPanel = $("<div>")
                            .addClass('result')
                            .appendTo($resultsPanel);

                        /*
                        $("<h3>")
                            .text(item.title)
                            .appendTo($resultPanel);
                        $("<div>")
                            .addClass('content')
                            .html(item.content)
                            .appendTo($resultPanel);
                            */
                        $("<h3>")
                            .text(item.category)
                            .appendTo($resultPanel);
                        $("<div>")
                            .addClass('content')
                            .html(item.suggested_text)
                            .appendTo($resultPanel);
                    }
                },
                error: function (error) {
                    console.trace(error);
                }
            })
        });

});
