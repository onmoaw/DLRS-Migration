DLRS-Migration

The primary areainfo migration query,
INSERT INTO \[DLRSLSINF\_4103PRSDB].\[dbo].\[AREAINF]

(

&#x20;   \[COMCOD],

&#x20;   \[UNITCOD],

&#x20;   \[UNITNAM],

&#x20;   \[UNITREFS],

&#x20;   \[UNITTYPE],

&#x20;   \[UNITGINF],

&#x20;   \[RESANUM],

&#x20;   \[GEOCODE],

&#x20;   \[AREATYPE],

&#x20;   \[ROWTIME]

)

SELECT

&#x20;   \[COMCOD],

&#x20;   STUFF(CAST(\[UNITCOD] AS VARCHAR(50)), 1, 1, '7') AS \[UNITCOD],

&#x20;   \[UNITNAM],

&#x20;   \[UNITREFS],

&#x20;   \[UNITTYPE],

&#x20;   \[UNITGINF],

&#x20;   \[RESANUM],

&#x20;   \[GEOCODE],

&#x20;   \[AREATYPE],

&#x20;   \[ROWTIME]

FROM \[DLRSLSINF\_0000SYSDB].\[dbo].\[SYS\_AREAINF];

