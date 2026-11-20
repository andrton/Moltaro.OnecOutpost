# Moltaro.OnecOutpost
un read and write scripts in 1C:Enterprise 8.3 and BAS databases hidden in a back office. A Windows agent next to 1C dials out over HTTPS, runs signed scripts through the COM connector and returns JSON; an embeddable .NET server queues the commands and scales across nodes.
