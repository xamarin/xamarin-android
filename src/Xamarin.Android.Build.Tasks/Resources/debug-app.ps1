#
# How to read vars from a file
#
#  $vars = Get-Content -Raw ./settings.txt | ConvertFrom-StringData
#
# Access to them:
#
#  $vars.one
#  write-host "$($vars.one)"
