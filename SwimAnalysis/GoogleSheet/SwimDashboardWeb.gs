/**
 * Σημείο εισόδου του κεντρικού πίνακα (Swim Analytics) για ένα project ΜΟΝΟ για τον πίνακα, π.χ. το Apps Script μέσα στο Google
 * Sheet των αναλύσεων (Επεκτάσεις → Apps Script), μαζί με τα SwimRaceAnalysis.gs, SwimDashboard.gs και το HTML SwimDashboardPage.
 *
 * ΜΗΝ το βάλεις σε project που έχει ήδη doGet (π.χ. KOE App): εκεί, στην αρχή του υπάρχοντος doGet(e), πρόσθεσε τη γραμμή
 *   var swimPage = swimDashTryGet(e); if (swimPage) return swimPage;
 */

function doGet(e) {
  return swimDashboardGet(e);
}
