<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
</head>
<body>

<h1>MapRate Plugin for GG1MapChooser</h1>
<h2>Overview</h2>
<p>The <strong>MapRate Plugin</strong> integrates seamlessly with the GG1 MapChooser plugin, providing a system for players to rate maps during gameplay. This plugin dynamically adjusts map weights based on player feedback and ensures that ratings are stored in the database for further analysis.</p>

<h2>Features</h2>
<ul>
    <li>
        <strong>Admin Flag for Rating:</strong>
        <p>Define the admin flag required to rate maps using the <code>Flag</code> parameter in the configuration file. Leave it empty to allow all players to rate maps.</p>
    </li>
    <li>
        <strong>Commands</strong>
        <ul>
            <li>
                <strong>!ratemap</strong>
                <ul>
                    <li>
                        <strong>Menu-Based Rating:</strong>
                        <p>Players with the admin flag (e.g., <code>@css/maprate</code>) can type <code>!ratemap</code> in chat to open a menu with these options:</p>
                        <ul>
                            <li>0 - Remove!</li>
                            <li>1 - Not Good</li>
                            <li>2 - Okay</li>
                            <li>3 - Good</li>
                            <li>4 - Very Good</li>
                            <li>5 - Among the Best!</li>
                        </ul>
                    </li>
                    <li>
                        <strong>Direct Rating:</strong>
                        <p>Players can type <code>!ratemap &lt;rating_value&gt;</code> to submit a rating directly without opening the menu (e.g., <code>!rate 3</code>).</p>
                    </li>
                    <li>
                        <strong>Access Restriction:</strong>
                        <p>Players without the required flag will see this message: <code>Only Supporters can rate maps.</code></p>
                    </li>
                    <li>
                        <strong>Re-Rating:</strong>
                        <p>If a player rates a map they’ve already rated, the menu will display their previous rating along with the rating date.</p>
                    </li>
                </ul>
            </li>
            <li>
                <strong>!maprate</strong>
                <ul>
                    <li>
                        <p>Displays the average rating for the current map in chat:</p>
                        <blockquote>
                            Average map rating: x.xx. You have rated the map x [date]. Type !ratemap to give the map another rating.
                        </blockquote>
                    </li>
                    <li>
                        <strong>Available to All Players:</strong>
                        <p>No flag is required to use this command.</p>
                    </li>
                </ul>
            </li>
        </ul>
    </li>
    <li>
        <strong>Integration with GG1 MapChooser Database:</strong>
        <ul>
            <li>
                <strong>Dynamic Weight Assignment:</strong>
                <ul>
                    <li>If the <code>weight</code> field in <code>GGMCmaps.json</code> is empty, the average rating of the map will replace the weight value. If no ratings exist, the weight defaults to 1.</li>
                    <li>If the <code>weight</code> field is manually set, the manually defined value takes precedence.</li>
                </ul>
            </li>
        </ul>
    </li>
    <li>
        <strong>Rating Expiration (Feature Pending):</strong>
        <ul>
            <li>Ratings automatically reset after a configurable number of days.</li>
            <li>The expiration duration is set in the plugin’s configuration file.</li>
        </ul>
    </li>
</ul>

<h2>Configurable Parameters</h2>
<p>The plugin includes the following configurable values in its <code>.cfg</code> file:</p>
<ul>
    <li>
        <strong>Database Settings:</strong>
        <ul>
            <li><code>DatabaseHost</code></li>
            <li><code>DatabasePort</code></li>
            <li><code>DatabaseName</code></li>
            <li><code>DatabaseUser</code></li>
            <li><code>DatabasePassword</code></li>
        </ul>
    </li>
    <li><code>MapRateFlag</code> - Admin flag for players allowed to rate maps.</li>
    <li><code>MapsToPlayBeforeRate</code> - Number of games required on a map before players can rate it.</li>
    <li><code>RatingExpirationDays</code> - Number of days after which ratings expire (Feature Pending).</li>
    <li><code>IfPlayedMapExpirationDays</code> - Number of days of inactivity before requiring players to re-play maps (Feature Pending).</li>
    <li><code>RoundToRemindRate</code> - Round number to remind players to rate maps (Feature Pending).</li>
    <li><code>DaysBeforeExpirationToRemindRate</code> - Number of days before rating expiration to remind players to re-rate maps (Feature Pending).</li>
</ul>

<h2>Installation</h2>
<ul>
    <li>Download the MapRate Plugin and place it in your plugin directory.</li>
    <li>Start the plugin or restart the server to automatically create the configuration file in:
        <pre><code>csgo/addons/counterstrikesharp/configs/plugins/maprating/maprating.json</code></pre>
    </li>
    <li>Ensure the database is set up with the necessary user rights.</li>
    <li>Fill in the database parameters in the configuration file and modify other parameters as needed.</li>
    <li>Restart the plugin or server to create the necessary database tables.</li>
</ul>

<h2>Disclaimer</h2>
<p>The plugin is provided <strong>"as-is"</strong> and fulfills the specific requirements it was designed for. Suggestions for improvements are welcome and may lead to additional features in the future.</p>

</body>
</html>