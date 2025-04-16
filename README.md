<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>MapRate Plugin for GG1MapChooser</title> </head>
<body>

<h1>MapRate Plugin for GG1MapChooser</h1>

<h2>Overview</h2>
<p>The <strong>MapRate Plugin</strong> is an extension for the <a href="[Link_To_GG1MapChooser_If_Available]">GG1 MapChooser</a> plugin for Counter-Strike. It introduces a system allowing players to rate the maps they play. Based on this collective player feedback, the plugin dynamically adjusts map weights within GG1 MapChooser, influencing how frequently each map appears in map votes.</p>

<h2>Prerequisites</h2>
<ul>
    <li>Counter-Strike Server (CS:GO / CS2 - Specify which one)</li>
    <li>CounterStrikeSharp (or the relevant modding platform)</li>
    <li>GG1 MapChooser Plugin installed and configured.</li>
</ul>

<h2>Features</h2>
<ul>
    <li>
        <strong>Permission-Based Rating:</strong>
        <p>Control who can rate maps using the <code>MapRateFlag</code> parameter in the configuration file. Specify an admin flag (e.g., <code>@css/maprate</code>) or leave it empty to permit all players to rate.</p>
    </li>
    <li>
        <strong>Rating Management:</strong>
        <ul>
            <li>Player ratings are persistently stored in a database.</li>
            <li>Players must play a map a configurable number of times (<code>MapsToPlayBeforeRate</code>) before they are eligible to rate it.</li>
            <li>Ratings can expire after a set duration (<code>RatingExpirationDays</code>), prompting players to re-rate.</li>
            <li>The system calculates an average rating for each map based on all valid player ratings.</li>
        </ul>
    </li>
    <li>
        <strong>Rating Reminders:</strong>
        <ul>
            <li>Players receive reminders to rate the current map if they haven't rated it yet or if their existing rating is nearing expiration.</li>
            <li>Reminders can be delivered via chat messages, on-screen messages, or an interactive menu.</li>
            <li>Timing for reminders is configurable:
                <ul>
                    <li>At a specific round number (<code>RoundToRemindRate</code>).</li>
                    <li>A specific number of seconds after a round starts (<code>SecondsFromRoundStartToRemindRate</code>).</li>
                    <li>After a specific delay following a GunGame match win (<code>SecondsFromGunGameWinToRemindRate</code>).</li>
                    <li>A specific number of minutes after the map starts (<code>MinutesFromMapStartToRemindRate</code>).</li>
                </ul>
            </li>
             <li>Reminders can trigger a configurable number of days before a rating expires (<code>DaysBeforeExpirationToRemindRate</code>).</li>
        </ul>
    </li>
    <li>
        <strong>Commands:</strong> <ul>
            <li>
                <strong><code>!ratemap</code></strong>
                <ul>
                    <li>Opens a rating menu for players possessing the required admin flag (defined by <code>MapRateFlag</code>).</li>
                    <li><strong>Menu Options:</strong>
                        <ul>
                            <li>0 - Remove! (Effectively a very negative rating)</li>
                            <li>1 - Not Good</li>
                            <li>2 - Okay</li>
                            <li>3 - Good</li>
                            <li>4 - Very Good</li>
                            <li>5 - Among the Best!</li>
                        </ul>
                    </li>
                    <li><strong>Direct Rating:</strong> Players can bypass the menu by typing <code>!ratemap &lt;rating_value&gt;</code> (e.g., <code>!ratemap 3</code>) to submit their rating directly.</li>
                    <li><strong>Access Restriction:</strong> Players without the required flag will receive a message like: <code>Only Supporters can rate maps.</code> (Message may vary based on configuration/localization).</li>
                    <li><strong>Re-Rating Information:</strong> If a player attempts to rate a map they have previously rated, the menu will display their prior rating and the date it was submitted.</li>
                </ul>
            </li>
            <li>
                <strong><code>!maprate</code></strong>
                <ul>
                    <li>Displays the current map's average rating and the user's own rating (if available) in the chat.</li>
                    <li>Example Output:
                        <blockquote>Average map rating: 4.25. You have rated the map 5 [2024-10-26]. Type !ratemap to change your rating.</blockquote>
                         </li>
                    <li><strong>Available to All Players:</strong> This command does not require any special flags.</li>
                </ul>
            </li>
        </ul>
    </li>
    <li>
        <strong>Integration with GG1 MapChooser Database:</strong>
        <ul>
            <li>
                <strong>Dynamic Map Weight Assignment:</strong>
                <ul>
                    <li>The plugin interacts with the <code>weight</code> field in the GG1 MapChooser map configuration file (e.g., <code>GGMCmaps.json</code>).</li>
                    <li>If a map's <code>weight</code> is <strong>not explicitly set</strong> in the GG1 MapChooser config:
                         <ul>
                             <li>The calculated average rating from this MapRate plugin is used as the weight.</li>
                             <li>If no ratings exist for the map yet, GG1 MapChooser uses its own default weight setting.</li>
                         </ul>
                    </li>
                    <li>If a map's <code>weight</code> <strong>is manually set</strong> in the GG1 MapChooser config, that manually defined value takes precedence and overrides the MapRate average.</li>
                    <li><strong>Recommendation:</strong> Configure GG1 MapChooser's default map weight to <code>3</code>. This aligns with the midpoint of MapRate's 0-5 scale, ensuring maps rated above 3 appear more frequently in votes, while those rated below 3 appear less often.</li>
                </ul>
            </li>
        </ul>
    </li>
    <li>
        <strong>Rating Expiration:</strong> <ul>
            <li>To keep feedback relevant, ratings can automatically expire after a configurable period (<code>RatingExpirationDays</code>).</li>
            <li>Players will be prompted to rate the map again after expiration and playing it sufficiently.</li>
            <li>An inactivity threshold (<code>IfPlayedMapExpirationDays</code>) can require players to replay maps if they haven't played them recently, potentially invalidating old ratings.</li>
            </ul>
    </li>
</ul>

<h2>Configuration</h2>
<p>Plugin settings are managed in the configuration file located at:
    <pre><code>csgo/addons/counterstrikesharp/configs/plugins/maprating/maprating.json</code></pre> </p>
<p>Key configurable parameters include:</p>
<ul>
    <li>
        <strong>Database Connection:</strong>
        <ul>
            <li><code>DatabaseHost</code></li>
            <li><code>DatabasePort</code></li>
            <li><code>DatabaseName</code></li>
            <li><code>DatabaseUser</code></li>
            <li><code>DatabasePassword</code></li>
        </ul>
    </li>
    <li><strong>Permissions & Eligibility:</strong>
        <ul>
            <li><code>MapRateFlag</code>: The admin flag required to use the <code>!ratemap</code> command (leave empty for all players).</li>
            <li><code>MinutesToPlayOnMapToRecordPlayed</code>: Minimum time (in minutes) a player must be on a team for their playtime on the map to count towards eligibility.</li>
            <li><code>MapsToPlayBeforeRate</code>: How many times a player must have played a map (meeting the minimum time criteria) before they can rate it.</li>
        </ul>
    </li>
    <li><strong>Rating Lifecycle:</strong>
        <ul>
            <li><code>RatingExpirationDays</code>: Number of days until a player's rating for a map expires.</li>
            <li><code>IfPlayedMapExpirationDays</code>: Period of inactivity (in days) on a map after which a player might need to replay it before their rating is considered valid or before they can rate again.</li>
        </ul>
    </li>
     <li><strong>Rating Reminders:</strong>
        <ul>
            <li><code>RoundToRemindRate</code>: Trigger reminder based on this round number.</li>
            <li><code>SecondsFromRoundStartToRemindRate</code>: Trigger reminder this many seconds after a round starts.</li>
            <li><code>RoundStartRemindMenu</code>: If reminding at round start, use <code>true</code> to show the rating menu directly, <code>false</code> to show text messages (chat/center screen).</li>
            <li><code>SecondsFromGunGameWinToRemindRate</code>: Delay (in seconds) after a GunGame win before showing the rating reminder/menu.</li>
            <li><code>MinutesFromMapStartToRemindRate</code>: Trigger reminder this many minutes after the map initially loads.</li>
            <li><code>DaysBeforeExpirationToRemindRate</code>: Start reminding players to re-rate a map this many days before their current rating expires.</li>
        </ul>
    </li>
</ul>

<h2>Installation</h2>
<ol> <li>Ensure all prerequisites (CounterStrikeSharp, GG1 MapChooser) are installed and working.</li>
    <li>Download the latest release of the MapRate Plugin.</li>
    <li>Place the plugin files into your CounterStrikeSharp plugins directory (<code>csgo/addons/counterstrikesharp/plugins/MapRate/</code> - adjust path as needed).</li>
    <li>Restart your server or load the plugin via console commands. This should automatically generate the default configuration file:
        <pre><code>csgo/addons/counterstrikesharp/configs/plugins/maprating/maprating.json</code></pre>
    </li>
    <li>Set up a database (e.g., MySQL, PostgreSQL, SQLite - specify supported types if known) and ensure you have the necessary connection details (host, port, user, password, database name) and grant appropriate permissions to the database user.</li>
    <li>Edit the generated <code>maprating.json</code> configuration file, filling in your database credentials and customizing other parameters as desired.</li>
    <li>Restart the server or reload the plugin for the configuration changes to take effect and for the plugin to create the necessary database tables.</li>
</ol>

<h2>Disclaimer</h2>
<p>This plugin is provided <strong>"as-is"</strong>. It was developed to meet a specific set of requirements. While suggestions for improvements or new features are welcome, there is no guarantee of future updates.</p>

</body>
</html>