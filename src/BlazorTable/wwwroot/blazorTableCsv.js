function GetCsvText(csvHolderElement) {
    try {
        console.log("called")
        const rows = $(csvHolderElement).find(".csvRow");
        let csvString = [];

        for (let i = 0; i < rows.length; i++) {
            const row = rows[i];
            const cols = $(row).find(".csvColumn").toArray();
            const rowText = cols.reduce((agg, col) => `${agg},${$(col).text()}`, "").replace(",", "");
            csvString.push(rowText);
        }

        const csvTextContent = csvString.join("\r\n");
        console.log(csvTextContent)

        FileSaveAs("report.csv", csvTextContent, "text/csv");
    }
    catch (error){
        console.log(error)
    }
}