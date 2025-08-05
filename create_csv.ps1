$csv = @"
TrackCircuitName,Zone
TH75_1RET,ゾーン1
TH75_1RT,ゾーン1
TH76_5LAT,ゾーン1
TH76_5LBT,ゾーン1
TH76_5LCT,ゾーン1
TH76_5LDT,ゾーン1
"@
$csv | Out-File -FilePath "ZoneMapping_Clean.csv" -Encoding UTF8
