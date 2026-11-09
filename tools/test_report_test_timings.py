import unittest
from decimal import Decimal
from tools.report_test_timings import duration_seconds, markdown, parse_receipt


def receipt(results, namespace=True):
    ns = ' xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"' if namespace else ''
    definitions = ''.join(f'<UnitTest id="{index}"><TestMethod className="{name}" /></UnitTest>'
                          for index, (name, _) in enumerate(results) if name is not None)
    rows = ''.join(f'<UnitTestResult testId="{index}" outcome="Passed"' +
                   (f' duration="{duration}"' if duration is not None else '') + ' />'
                   for index, (_, duration) in enumerate(results))
    return (f'<TestRun{ns}><Times start="2026-11-09T10:00:00Z" finish="2026-11-09T10:00:08Z" />'
            f'<TestDefinitions>{definitions}</TestDefinitions><Results>{rows}</Results>'
            f'<ResultSummary><Counters total="{len(results)}" /></ResultSummary></TestRun>').encode()


class TimingReportTests(unittest.TestCase):
    def test_parallel_case_sum_is_distinct_from_receipt_wall_elapsed(self):
        profile = parse_receipt(receipt([('Invented.A', '00:00:05'), ('Invented.A', '00:00:07'), ('Invented.B', '00:00:02')]))
        self.assertEqual(Decimal(14), profile.case_seconds)
        self.assertEqual(Decimal(8), profile.wall_seconds)
        self.assertEqual(('Invented.A', 2, Decimal(12), Decimal(7)),
                         (profile.classes[0].name, profile.classes[0].cases, profile.classes[0].seconds, profile.classes[0].maximum))
        self.assertIn('not elapsed time or measured savings', markdown(profile, 'synthetic'))

    def test_fixture_setup_is_not_silently_folded_into_case_durations(self):
        profile = parse_receipt(receipt([('Invented.A', '00:00:01')]))
        self.assertEqual(Decimal(1), profile.classes[0].seconds)
        self.assertEqual(Decimal(8), profile.wall_seconds)
        self.assertIn('Class setup/teardown can sit outside case durations', markdown(profile, 'synthetic'))
    def test_missing_metadata_is_disclosed_without_guessing_display_names(self):
        profile = parse_receipt(receipt([(None, '00:00:01'), ('Invented.A', None)]))
        self.assertEqual(1, profile.unmapped)
        self.assertEqual(1, profile.missing_durations)
        self.assertEqual(2, profile.results)
        self.assertEqual(2, profile.declared_total)

    def test_namespaceless_receipt_and_seven_digit_fraction_are_preserved(self):
        profile = parse_receipt(receipt([('Invented.A', '00:00:00.1234567')], namespace=False))
        self.assertEqual(Decimal('0.1234567'), profile.case_seconds)
        self.assertEqual(Decimal('93784.0000001'), duration_seconds('1.02:03:04.0000001'))

    def test_top_limit_is_stable_and_does_not_change_totals(self):
        profile = parse_receipt(receipt([(f'Invented.C{n:02}', f'00:00:{n:02}') for n in range(21)]))
        text = markdown(profile, 'synthetic', 20)
        self.assertIn('| Invented.C20 |', text)
        self.assertNotIn('| Invented.C00 |', text)
        self.assertEqual(21, profile.results)
        self.assertEqual(Decimal(210), profile.case_seconds)

    def test_malformed_durations_refuse_instead_of_silently_becoming_zero(self):
        for duration in ['bad', '-00:00:01', '00:60:00', '00:00:60']:
            with self.subTest(duration=duration), self.assertRaises(ValueError):
                duration_seconds(duration)


if __name__ == '__main__':
    unittest.main()
