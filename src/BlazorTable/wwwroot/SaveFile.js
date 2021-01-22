function FileSaveAs(filename, fileContent, mimeType = "application/pdf;base64") {
    //build link
    var link = document.createElement('a');
    link.download = filename;
    link.href = `data:${mimeType},${encodeURIComponent(fileContent)}`;
    console.log(link.href.slice(0, 200));
    //webkitURL.createObjectURL(fileBlob);

    //append link, click, and remove
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}

////this "works" for the table data that is visible, won't get the rest of it.
//function MakeCSV(tableRef) {
//    var trs = $(tableRef).find('tr').toArray();
//    data = trs.reduce((agg, tr) => {
//        return agg + $(tr).find("td").toArray().reduce((s, td) => {
//            return s + $(td).html() + ",";
//        }, "") + "\n";
//    }, "");
//    return data;
//}