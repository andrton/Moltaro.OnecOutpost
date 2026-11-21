// Posting writes one receipt of the product to the stock register. The register records are
// written only when Write is set, since the document writes the selected record sets.

Procedure Posting(Cancel, PostingMode)

	RegisterRecords.Stock.Write = True;

	Record = RegisterRecords.Stock.Add();
	Record.RecordType = AccumulationRecordType.Receipt;
	Record.Period = Date;
	Record.Product = Product;
	Record.Quantity = Quantity;

EndProcedure
