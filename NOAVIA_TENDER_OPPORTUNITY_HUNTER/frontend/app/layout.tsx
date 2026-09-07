import "./globals.css";

export const metadata = {
  title: "Noavia Tender Hunter",
  description: "Procurement intelligence for German public tenders"
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
