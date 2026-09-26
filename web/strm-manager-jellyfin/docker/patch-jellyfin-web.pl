use strict;
use warnings;

my $index_path = '/jellyfin/jellyfin-web/index.html';

my $stylesheet_tag =
    '<link rel="stylesheet" href="strm-manager/strm-manager-jellyfin.css">';

my $script_tag =
    '<script defer="defer" src="strm-manager/strm-manager.js"></script>';

open my $input, '<', $index_path
    or die "Cannot open $index_path: $!\n";

local $/;
my $html = <$input>;
close $input;

sub count_occurrences {
    my ($text, $needle) = @_;
    return () = $text =~ /\Q$needle\E/g;
}

die "Expected exactly one </head>\n"
    unless count_occurrences(
        $html,
        '</head>',
    ) == 1;

die "Expected exactly one </body>\n"
    unless count_occurrences(
        $html,
        '</body>',
    ) == 1;

die "Expected exactly one </html>\n"
    unless count_occurrences(
        $html,
        '</html>',
    ) == 1;

die "Expected exactly one Jellyfin reactRoot\n"
    unless count_occurrences(
        $html,
        'id="reactRoot"',
    ) == 1;

die "STRM Manager stylesheet is already present\n"
    unless count_occurrences(
        $html,
        'strm-manager-jellyfin.css',
    ) == 0;

die "STRM Manager bootstrap is already present\n"
    unless count_occurrences(
        $html,
        'strm-manager.js',
    ) == 0;

$html =~ s{
    \Q</head>\E
}{
    $stylesheet_tag</head>
}x;

$html =~ s{
    \Q</body>\E
}{
    $script_tag</body>
}x;

die "Failed to inject exactly one STRM Manager stylesheet\n"
    unless count_occurrences(
        $html,
        'strm-manager-jellyfin.css',
    ) == 1;

die "Failed to inject exactly one STRM Manager bootstrap\n"
    unless count_occurrences(
        $html,
        'strm-manager.js',
    ) == 1;

open my $output, '>', $index_path
    or die "Cannot write $index_path: $!\n";

print {$output} $html;
close $output;

print
    "STRM Manager Jellyfin Web assets injected successfully.\n";