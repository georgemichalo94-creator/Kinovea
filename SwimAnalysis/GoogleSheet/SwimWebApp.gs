/**
 * Σημεία εισόδου για το Swim Race Analysis.
 *
 * Βάλε αυτό το αρχείο ΜΟΝΟ αν το project ΔΕΝ έχει ήδη συναρτήσεις onOpen, doGet ή doPost
 * (π.χ. νέο, άδειο Google Sheet). Αν το project τις έχει ήδη (π.χ. KOE App), ΜΗΝ το προσθέσεις, αλλά:
 *   - στο υπάρχον onOpen(e) πρόσθεσε τη γραμμή:   swimOnOpen(e);
 *     (ή απλά τρέξε το swimSetup: αν το onOpen δεν καλεί το swimOnOpen, βάζει αυτόματα trigger για το μενού)
 *   - στην αρχή του υπάρχοντος doPost(e) πρόσθεσε:
 *       var swimResponse = swimTryHandlePost(e); if (swimResponse) return swimResponse;
 */

function onOpen(e) {
  swimOnOpen(e);
}

function doGet(e) {
  return swimDoGet(e);
}

function doPost(e) {
  return swimDoPost(e);
}
