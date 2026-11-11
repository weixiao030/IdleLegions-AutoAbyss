import json
from pathlib import Path
import tempfile
import unittest

from collect_card_catalog import collect_cards


class BadgeReceiptTests(unittest.TestCase):
    def test_unselected_badges_use_their_actual_menu_context(self):
        card = dict(id='es2_mordaza', nombre='Mordaza', desc='Observed effect')
        receipt = dict(snapshot=dict(sima=dict(run=dict(blasonPendiente=dict(
            familia='escarcha', umbral=2, opciones=[card]
        )))))
        # Serialized command responses contain the same nested menus.
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'receipt.json'
            path.write_text(json.dumps(dict(response=json.dumps(receipt))), encoding='utf-8')
            cards, sources = collect_cards([path])
        self.assertEqual(cards[card['id']], {**card, 'familia': 'escarcha', 'umbral': 2})
        self.assertEqual(sources[card['id']], {str(path.resolve())})

    def test_context_does_not_spread_to_unrelated_nested_objects(self):
        receipt = dict(familia='escarcha', umbral=2, opciones=[dict(
            id='badge', nombre='Badge', desc='Observed',
            unrelated=dict(id='unrelated', nombre='Other', desc='No family')
        )], unrelated=[dict(id='sibling', nombre='Sibling', desc='No family')])
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'receipt.json'
            path.write_text(json.dumps(receipt), encoding='utf-8')
            cards, _ = collect_cards([path])
        self.assertEqual(set(cards), {'badge'})

    def test_explicit_card_fields_take_precedence_over_menu_defaults(self):
        card = dict(id='relic', familia='fulgor', umbral=4, nombre='Card', desc='Observed')
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'receipt.json'
            path.write_text(json.dumps(dict(familia='escarcha', umbral=2, opciones=[card])), encoding='utf-8')
            cards, _ = collect_cards([path])
        self.assertEqual(cards['relic'], card)


if __name__ == '__main__':
    unittest.main()
