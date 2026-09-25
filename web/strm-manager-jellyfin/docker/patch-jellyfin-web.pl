use strict;
use warnings;

my $index_path = '/jellyfin/jellyfin-web/index.html';
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

die "Expected exactly one </body>\n"
    unless count_occurrences($html, '</body>') == 1;

die "Expected exactly one </html>\n"
    unless count_occurrences($html, '</html>') == 1;

die "Expected exactly one Jellyfin reactRoot\n"
    unless count_occurrences($html, 'id="reactRoot"') == 1;

die "STRM Manager bootstrap is already present\n"
    unless count_occurrences($html, 'strm-manager.js') == 0;

$html =~ s{\Q</body>\E}{$script_tag</body>};

die "Failed to inject exactly one STRM Manager bootstrap\n"
    unless count_occurrences($html, 'strm-manager.js') == 1;

open my $output, '>', $index_path
    or die "Cannot write $index_path: $!\n";

print {$output} $html;
close $output;

print "STRM Manager Jellyfin Web bootstrap injected successfully.\n";