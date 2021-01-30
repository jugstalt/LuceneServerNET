Usage:
CreateIndex.exe -server[-s] server
                -index[-i] indexname
                -field fieldname[.fieldtype][.stored|not_stored] // add indexed field - defaults .Text.stored
                -field ...
                -storedfield[-sfield] fieldname[.fieldtype]  // add stored field - defaults .Text
                -storedfield ...
                -primary primary-search-fieldname  // default: first field
                -remove  // remove existing index first

Example (Powershell):

.\CreateIndex.exe -s https://localhost:44393    `
                  -i new-index                  `
                  -f suggested_text             `
                  -f sub_text                   `
                  -f category                   `
                  -sfield id        


