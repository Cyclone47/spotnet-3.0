$spotnetDir = 'd:\sourcecode\src\Spotnet\Spotnet'

# Fix Event references in C# files
$csFiles = Get-ChildItem -Path $spotnetDir -Filter *.cs -Recurse
foreach ($file in $csFiles) {
    $code = [System.IO.File]::ReadAllText($file.FullName)
    $orig = $code
    $code = $code -replace 'MenuItem\.Click(?!\w)', 'MenuItem.ClickEvent'
    $code = $code -replace 'Button\.Click(?!\w)', 'Button.ClickEvent'
    $code = $code -replace 'UIElement\.PreviewMouseDown(?!\w)', 'UIElement.PreviewMouseDownEvent'
    $code = $code -replace 'UIElement\.PreviewMouseUp(?!\w)', 'UIElement.PreviewMouseUpEvent'
    $code = $code -replace 'UIElement\.MouseDown(?!\w)', 'UIElement.MouseDownEvent'
    $code = $code -replace 'UIElement\.MouseUp(?!\w)', 'UIElement.MouseUpEvent'

    if ($code -ne $orig) {
        [System.IO.File]::WriteAllText($file.FullName, $code)
        Write-Host "Fixed routed events in $($file.Name)"
    }
}

# Fix VolumeSlider.xaml to have public field modifier
$volXaml = "$spotnetDir\downloader\controls\volumeslider.xaml"
if (Test-Path $volXaml) {
    $xaml = [System.IO.File]::ReadAllText($volXaml)
    $xaml = $xaml.Replace('Name="VolumeWithMuteSlider"', 'x:Name="VolumeWithMuteSlider" x:FieldModifier="public"')
    [System.IO.File]::WriteAllText($volXaml, $xaml)
    Write-Host "Updated VolumeSlider.xaml with public field modifier."
}
